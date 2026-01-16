using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace Middleware_console
{
    public class GeminiCore
    {
        private const string GeminiApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=" + Secrets.ApiKey;

        private Dictionary<string, string> promptTemplates = new Dictionary<string, string>();

        public GeminiCore()
        {
            LoadPromptTemplates();
        }
        // --- 1. Load Templates ---
        private void LoadPromptTemplates()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PromptTemplates.json");
            try
            {
                if (!File.Exists(filePath))
                {
                    LogError($"Critical: File not found -> {filePath}");
                    return;
                }

                string jsonText = File.ReadAllText(filePath);
                var collection = JsonConvert.DeserializeObject<TemplateCollection>(jsonText);

                promptTemplates.Clear();
                if (collection?.templates != null)
                {
                    foreach (var template in collection.templates)
                    {
                        string fullPrompt = string.Join(Environment.NewLine, template.prompt_lines);
                        promptTemplates.Add(template.key, fullPrompt);
                    }
                }
                Console.WriteLine($"[SYSTEM] Loaded {promptTemplates.Count} templates successfully.");
            }
            catch (Exception ex)
            {
                LogError($"Error loading 'PromptTemplates.json': {ex.Message}");
            }
        }

        public string BuildPlcPrompt(string chuyenMuc, string hangPLC, string loaiPLC, string loaiKhoi, string ngonNgu, string yeuCauLogic, string userTagsContent)
        {

            string key = "";

            if (chuyenMuc == "PLC Programming")
            {
                string blockKey = "";
                string langKey = "";

                // 1. Xác định Loại Khối (FB, FC, LAD, FBD)
                if (loaiKhoi.StartsWith("FUNCTION (FC)")) blockKey = "FC";
                else if (loaiKhoi.StartsWith("FUNCTION_BLOCK (FB)")) blockKey = "FB";
                else // "Không (None)"
                {
                    if (ngonNgu.StartsWith("Ladder")) blockKey = "LAD";
                    else if (ngonNgu.StartsWith("FBD")) blockKey = "FBD";
                    else blockKey = "FB"; // Fallback (SCL/STL + None -> default to FB)
                }

                // 2. Xác định Ngôn ngữ (chỉ áp dụng cho FB/FC)
                if (blockKey == "FB" || blockKey == "FC")
                {
                    if (ngonNgu.StartsWith("SCL")) langKey = "_SCL";
                    else if (ngonNgu.StartsWith("STL")) langKey = "_STL";
                    else langKey = "_SCL"; // Mặc định là SCL nếu chọn LAD/FBD + FB/FC
                }

                key = $"{hangPLC}_{blockKey}{langKey}"; // e.g., "Siemens_FC_SCL", "Siemens_LAD"
            }
            else // "Lập trình SCADA"
            {
                // (Sau này sẽ đọc từ cbScadaPlatform...)
                key = "WinCC_Unified_Layout"; // Hardcoded
            }

            // 3. Tra cứu template
            string template;
            if (!promptTemplates.TryGetValue(key, out template))
            {
                // Logic Fallback (Tìm template dự phòng)
                string fallbackKey = "Siemens_FB_SCL"; // Dự phòng an toàn nhất
                if (chuyenMuc == "PLC Programming")
                {
                    if (key.Contains("LAD")) fallbackKey = "Siemens_LAD";
                    else if (key.Contains("FBD")) fallbackKey = "Siemens_FBD";
                    else if (key.Contains("_FC_")) fallbackKey = "Siemens_FC_SCL";
                    else if (key.Contains("_FB_")) fallbackKey = "Siemens_FB_SCL";
                }

                LogError($"No template for key can be found: '{key}'.\nUsing default template '{fallbackKey}'.");
                if (!promptTemplates.TryGetValue(fallbackKey, out template))
                {
                    LogError($"Critical error:no default template can be found '{fallbackKey}'.");
                    return "ERROR: NO TEMPLATE CAN BE FOUND";
                }
            }

            string syntaxRules = "";
            try
            {
                // Giả sử file để ở thư mục gốc của ứng dụng
                syntaxRules = File.ReadAllText("SclSyntaxRules.txt");
            }
            catch
            {
                // Fallback nếu không tìm thấy file (để tránh crash)
                syntaxRules = "Note: Use standard Siemens SCL syntax.";
            }

            // 4. Thay thế placeholder
            template = template.Replace("%HANG_PLC%", hangPLC);
            template = template.Replace("%LOAI_PLC%", loaiPLC);
            template = template.Replace("%NGON_NGU%", ngonNgu);
            template = template.Replace("%LOAI_KHOI%", loaiKhoi);
            template = template.Replace("%LOGIC_HERE%", yeuCauLogic);
            template = template.Replace("%SCL_SYNTAX%", syntaxRules);
            template = template.Replace("%USER_TAGS%", userTagsContent);

            return template;
        }

        // --- (Các hàm còn lại: GenerateScriptFromGemini, ExtractCodeFromMarkdown) ---
        #region API Calls & File Handling
        // --- 3. Call API (Đã sửa lỗi hiển thị) ---
        public async Task<string> GenerateScriptFromGemini(string prompt)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var requestBody = new
                    {
                        contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
                        generationConfig = new { temperature = 0.4, maxOutputTokens = 8192 }
                    };

                    string jsonBody = JsonConvert.SerializeObject(requestBody);
                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(GeminiApiUrl, content);

                    if (!response.IsSuccessStatusCode)
                    {
                        string errorDetails = await response.Content.ReadAsStringAsync();
                        LogError($"API Error {response.StatusCode}: {errorDetails}");
                        return null;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseBody);

                    // Kiểm tra lỗi từ Google
                    if (jsonResponse.error != null)
                    {
                        LogError($"API Message: {jsonResponse.error.message}");
                        return null;
                    }

                    if (jsonResponse.candidates == null || jsonResponse.candidates.Count == 0)
                    {
                        if (jsonResponse.promptFeedback != null)
                        {
                            LogError($"Prompt Blocked: {jsonResponse.promptFeedback.blockReason}");
                        }
                        return null;
                    }

                    string rawScript = jsonResponse.candidates[0].content.parts[0].text.ToString();
                    rawScript = rawScript.Replace("\\n", Environment.NewLine).Replace("\\\"", "\"");

                    return ExtractCodeFromMarkdown(rawScript);
                }
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error: {ex.Message}");
                return null;
            }
        }

        private string ExtractCodeFromMarkdown(string rawResponse)
        {
            try
            {
                var match = Regex.Match(rawResponse, @"```(?:[a-z]+)?\s*([\s\S]*?)\s*```");
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                {
                    return match.Groups[1].Value.Trim();
                }
                return rawResponse.Trim();
            }
            catch
            {
                return rawResponse;
            }
        }

        // --- 4. Save File (SỬA LỖI: Truyền tham số category/language) ---
        public string SaveScriptToFile(string scriptContent, string chuyenMuc, string ngonNgu)
        {
            try
            {
                string name = "Code";
                string fileExtension = ".txt";

                // Logic xác định đuôi file
                if (chuyenMuc == "SCADA Designing")
                {
                    fileExtension = ".json";
                    name = "SCADA_Layout";
                }
                else if (ngonNgu.StartsWith("SCL")) { fileExtension = ".scl"; name = "SCL"; }
                else if (ngonNgu.StartsWith("STL")) { fileExtension = ".awl"; name = "STL"; }
                else if (ngonNgu.StartsWith("Ladder")) { fileExtension = "_lad.txt"; name = "LAD"; }
                else if (ngonNgu.StartsWith("FBD")) { fileExtension = "_fbd.txt"; name = "FBD"; }

                string exeFolder = AppDomain.CurrentDomain.BaseDirectory;
                string saveFolder = Path.Combine(exeFolder, "saved_code");

                if (!Directory.Exists(saveFolder)) Directory.CreateDirectory(saveFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"Generated_{name}_{timestamp}{fileExtension}";
                string fullPath = Path.Combine(saveFolder, fileName);

                File.WriteAllText(fullPath, scriptContent);

                return fullPath; // Trả về đường dẫn để Program biết
            }
            catch (Exception ex)
            {
                LogError($"Error saving file: {ex.Message}");
                return null;
            }
        }

        // --- Helper: Hàm in lỗi màu đỏ cho gọn code ---
        private void LogError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red; // Đổi màu đỏ
            Console.WriteLine($"[ERROR] {msg}");       // In nội dung kèm tiền tố [ERROR]
            Console.ResetColor();                       // Reset màu
        }
        #endregion
    }
    public class PromptTemplate
    {
        public string key { get; set; }
        public System.Collections.Generic.List<string> prompt_lines { get; set; }
    }
    public class TemplateCollection
    {
        public System.Collections.Generic.List<PromptTemplate> templates { get; set; }
    }
}
