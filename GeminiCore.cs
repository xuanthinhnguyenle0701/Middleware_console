using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading; // Thêm để dùng Thread.Sleep

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
            // (Giữ nguyên code cũ của bạn đoạn này)
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
                // Console.WriteLine($"[SYSTEM] Loaded {promptTemplates.Count} templates successfully."); // Tắt bớt log cho gọn
            }
            catch (Exception ex)
            {
                LogError($"Error loading 'PromptTemplates.json': {ex.Message}");
            }
        }

        // --- 2. Build Prompt ---
        public string BuildPlcPrompt(string chuyenMuc, string hangPLC, string loaiPLC, string loaiKhoi, string ngonNgu, string yeuCauLogic, string userTagsContent)
        {
            // (Giữ nguyên code cũ của bạn đoạn này)
            string key = "";
            if (chuyenMuc == "PLC Programming")
            {
                string blockKey = "";
                string langKey = "";
                if (loaiKhoi.StartsWith("FUNCTION (FC)")) blockKey = "FC";
                else if (loaiKhoi.StartsWith("FUNCTION_BLOCK (FB)")) blockKey = "FB";
                else 
                {
                    if (ngonNgu.StartsWith("Ladder")) blockKey = "LAD";
                    else if (ngonNgu.StartsWith("FBD")) blockKey = "FBD";
                    else blockKey = "FB"; 
                }

                if (blockKey == "FB" || blockKey == "FC")
                {
                    if (ngonNgu.StartsWith("SCL")) langKey = "_SCL";
                    else if (ngonNgu.StartsWith("STL")) langKey = "_STL";
                    else langKey = "_SCL"; 
                }
                key = $"{hangPLC}_{blockKey}{langKey}";
            }
            else 
            {
                key = "WinCC_Unified_Layout"; 
            }

            string template;
            if (!promptTemplates.TryGetValue(key, out template))
            {
                string fallbackKey = "Siemens_FB_SCL";
                if (chuyenMuc == "PLC Programming")
                {
                    if (key.Contains("LAD")) fallbackKey = "Siemens_LAD";
                    else if (key.Contains("FBD")) fallbackKey = "Siemens_FBD";
                    else if (key.Contains("_FC_")) fallbackKey = "Siemens_FC_SCL";
                    else if (key.Contains("_FB_")) fallbackKey = "Siemens_FB_SCL";
                }
                // LogError($"No template for key: '{key}'. Using fallback '{fallbackKey}'."); // Có thể bỏ comment nếu muốn debug
                if (!promptTemplates.TryGetValue(fallbackKey, out template))
                {
                    LogError($"Critical error: no default template found '{fallbackKey}'.");
                    return "ERROR: NO TEMPLATE CAN BE FOUND";
                }
            }

            string syntaxRules = "";
            try { syntaxRules = File.ReadAllText("SclSyntaxRules.txt"); }
            catch { syntaxRules = "Note: Use standard Siemens SCL syntax."; }

            template = template.Replace("%HANG_PLC%", hangPLC);
            template = template.Replace("%LOAI_PLC%", loaiPLC);
            template = template.Replace("%NGON_NGU%", ngonNgu);
            template = template.Replace("%LOAI_KHOI%", loaiKhoi);
            template = template.Replace("%LOGIC_HERE%", yeuCauLogic);
            template = template.Replace("%SCL_SYNTAX%", syntaxRules);
            template = template.Replace("%USER_TAGS%", userTagsContent);

            return template;
        }

        // --- 3. Call API ---
        public async Task<string> GenerateScriptFromGemini(string prompt)
        {
            // (Giữ nguyên code cũ của bạn đoạn này)
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

                    if (jsonResponse.error != null) { LogError($"API Message: {jsonResponse.error.message}"); return null; }
                    if (jsonResponse.candidates == null || jsonResponse.candidates.Count == 0) return null;

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
            catch { return rawResponse; }
        }

        // --- 4. Save File ---
        public string SaveScriptToFile(string scriptContent, string chuyenMuc, string ngonNgu)
        {
            // (Giữ nguyên code cũ của bạn đoạn này)
            try
            {
                string name = "Code";
                string fileExtension = ".txt";

                if (chuyenMuc == "SCADA Designing") { fileExtension = ".json"; name = "SCADA_Layout"; }
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
                return fullPath;
            }
            catch (Exception ex)
            {
                LogError($"Error saving file: {ex.Message}");
                return null;
            }
        }

        private void LogError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {msg}");
            Console.ResetColor();
        }

        // ==================================================================================
        // --- 5. HÀM PROCESS AI (ĐƯỢC CHUYỂN TỪ NAVIGATOR VÀO ĐÂY) ---
        // ==================================================================================
        public async Task ProcessAI(string userPrompt, string mode)
        {
            try 
            {
                // 1. Chuẩn bị tham số
                string category = "PLC Programming";
                string lang = "SCL";
                string blockType = "FB";

                // Logic map mode
                if (mode == "SCADA")
                {
                    category = "SCADA Designing";
                    lang = ""; 
                }
                else if (mode == "STL")
                {
                    lang = "STL";
                }
                else if (mode == "FBD")
                {
                    blockType = "";
                    lang = "FBD";
                }
                else if (mode == "LAD")
                {
                    blockType = "";
                    lang = "LAD";
                }

                // 2. Build Prompt (Gọi trực tiếp hàm trong class này)
                // Giả sử Brand/PLC Type mặc định là Siemens S7-1500
                string fullPrompt = this.BuildPlcPrompt(category, "Siemens", "S7-1500", blockType, lang, userPrompt, "");

                // 3. Gọi API & Hiển thị Spinner
                // Lưu ý: Cần đảm bảo ConsoleUI là public static để gọi được từ đây
                var task = this.GenerateScriptFromGemini(fullPrompt);
                
                // Gọi Spinner từ ConsoleUI
                await ConsoleUI.ShowSpinner(task); 

                // 4. Lấy kết quả
                string code = await task;

                if (!string.IsNullOrEmpty(code))
                {
                    ConsoleUI.PrintSuccess("Code generated successfully!");

                    // 5. Lưu file
                    string savedPath = this.SaveScriptToFile(code, category, lang);

                    if (!string.IsNullOrEmpty(savedPath))
                    {
                        // 6. Hỏi User mở file (Tương tác UI ngay trong luồng xử lý)
                        string openChoice = ConsoleUI.SelectOption("File saved. What next?", new[] {
                            "Open File now",
                            "Continue"
                        });

                        if (openChoice.Contains("Open"))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start("explorer.exe", savedPath);
                            }
                            catch { ConsoleUI.PrintError("Could not open file."); }
                        }
                    }
                }
                else
                {
                    ConsoleUI.PrintError("AI failed to generate code (Empty response or Error).");
                    Thread.Sleep(2000); 
                }
            }
            catch (Exception ex)
            {
                LogError($"ProcessAI Error: {ex.Message}");
            }
        }
    }

    // Class JSON Models
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