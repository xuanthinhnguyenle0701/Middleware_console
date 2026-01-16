using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Middleware_console
{
    public enum AppState
    {
        MainMenu,
        
        // Nhóm AI
        AI_Menu,
        AI_InputLogic,
        AI_Processing,

        // Nhóm TIA Automation (Mới thêm)
        TIA_Menu,
        TIA_Processing,

        Exit
    }

    internal class Navigator
    {
        // Khởi tạo 2 Engine: 1 cho AI, 1 cho TIA
        private static GeminiCore _aiCore = new GeminiCore();
        private static TIA_V20 _tiaEngine = new TIA_V20(); // Class copy từ bài cũ sang

        private static string _lastGeneratedFilePath = "";
        private static string _currentMode = "";

        // Bỏ [STAThread] vì không dùng Form nữa
        static async Task Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            Console.OutputEncoding = System.Text.Encoding.UTF8; // Để in tiếng Việt có dấu nếu cần

            AppState currentState = AppState.MainMenu;

            while (currentState != AppState.Exit)
            {
                // Chỉ xóa màn hình khi không phải đang xử lý (để giữ log chạy)
                if (currentState != AppState.AI_Processing && currentState != AppState.TIA_Processing)
                    Console.Clear();

                switch (currentState)
                {
                    // --- 1. MENU CHÍNH ---
                    case AppState.MainMenu:
                        ConsoleUI.PrintHeader("GEMINI AI MIDDLEWARE");
                        string choice = ConsoleUI.SelectOption("Select Module:", new[] {
                            "1. AI Code Generator",
                            "2. TIA Portal Automation",
                            "3. Exit"
                        });

                        if (choice.Contains("1")) currentState = AppState.AI_Menu;
                        else if (choice.Contains("2")) currentState = AppState.TIA_Menu;
                        else currentState = AppState.Exit;
                        break;

                    // --- 2. MENU AI (Giữ nguyên) ---
                    case AppState.AI_Menu:
                        ConsoleUI.PrintHeader("MODULE: AI GENERATOR");
                        string aiChoice = ConsoleUI.SelectOption("Generate type:", new[] {
                            "SCL - Function Block",
                            "SCADA Layout (JSON)",
                            "Back to Main Menu"
                        });

                        if (aiChoice.Contains("Back")) currentState = AppState.MainMenu;
                        else
                        {
                            if (aiChoice.Contains("SCL")) _currentMode = "SCL";
                            else if (aiChoice.Contains("SCADA")) _currentMode = "SCADA";
                            currentState = AppState.AI_InputLogic;
                        }
                        break;

                    case AppState.AI_InputLogic:
                        ConsoleUI.PrintHeader($"INPUT FOR {_currentMode}");
                        string userPrompt = ConsoleUI.GetMultiLineInput("Enter requirements");
                        
                        ConsoleUI.PrintStep("Processing AI request...");
                        currentState = AppState.AI_Processing;
                        await ProcessAI(userPrompt, _currentMode);
                        currentState = AppState.AI_Menu;
                        break;

                    // --- 3. MENU TIA PORTAL (Logic thay thế WinForm) ---
                    case AppState.TIA_Menu:
                        ConsoleUI.PrintHeader("MODULE: TIA PORTAL AUTOMATION");
                        
                        // Kiểm tra trạng thái kết nối để hiển thị menu phù hợp
                        string status = _tiaEngine.IsConnected ? "[CONNECTED]" : "[DISCONNECTED]";
                        Console.WriteLine($"Status: {status}\n");

                        string tiaChoice = ConsoleUI.SelectOption("Action:", new[] {
                            "1. Connect to TIA Portal",
                            "2. Import SCL File to PLC", // Thay thế nút "Import"
                            "3. Export PLC Tags",
                            "4. Back to Main Menu"
                        });

                        if (tiaChoice.Contains("Back"))
                        {
                            currentState = AppState.MainMenu;
                        }
                        else if (tiaChoice.Contains("Connect"))
                        {
                            currentState = AppState.TIA_Processing;
                            ConnectTiaLogic(); // Hàm xử lý kết nối
                            currentState = AppState.TIA_Menu;
                        }
                        else if (tiaChoice.Contains("Import"))
                        {
                            currentState = AppState.TIA_Processing;
                            ImportSclLogic(); // Hàm xử lý import
                            currentState = AppState.TIA_Menu;
                        }
                         else if (tiaChoice.Contains("Export"))
                        {
                            // Bạn có thể tự thêm hàm ExportTagsLogic() tương tự
                            ConsoleUI.PrintStep("Feature coming soon...");
                            Thread.Sleep(1000);
                        }
                        break;
                }
            }
        }

        // --- CÁC HÀM XỬ LÝ LOGIC TIA ---

        static void ConnectTiaLogic()
        {
            ConsoleUI.PrintStep("Connecting to Running TIA Portal Instance...");
            try
            {
                // Gọi hàm Connect từ class TIA_V20 cũ của bạn
                // Giả sử hàm Connect() trả về bool hoặc void
                _tiaEngine.ConnectToTiaPortal(); 
                
                // Nếu class cũ của bạn không ném lỗi mà trả về bool, hãy check bool
                ConsoleUI.PrintSuccess("Connected to TIA Portal successfully!");
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Connection Failed: {ex.Message}");
            }
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        static void ImportSclLogic()
        {
            // 1. Hỏi đường dẫn file (Nếu vừa tạo xong thì gợi ý luôn)
            string defaultPath = _lastGeneratedFilePath;
            string filePath = "";

            if (!string.IsNullOrEmpty(defaultPath) && File.Exists(defaultPath))
            {
                Console.WriteLine($"Found recently generated file: {Path.GetFileName(defaultPath)}");
                string useLast = ConsoleUI.SelectOption("Use this file?", new[] { "Yes", "No, browse another" });
                if (useLast == "Yes") filePath = defaultPath;
            }

            if (string.IsNullOrEmpty(filePath))
            {
                Console.Write("Enter full path to .scl file: ");
                filePath = Console.ReadLine().Replace("\"", ""); // Xóa dấu ngoặc kép nếu user copy path
            }

            if (!File.Exists(filePath))
            {
                ConsoleUI.PrintError("File not found!");
                Thread.Sleep(1500);
                return;
            }

            // 2. Thực hiện Import (Gọi hàm từ TIA_V20.cs)
            ConsoleUI.PrintStep($"Importing {Path.GetFileName(filePath)} to PLC...");
            try
            {
                // Giả sử TIA_V20.cs của bạn có hàm ImportBlock(filePath)
                // Bạn cần mở file TIA_V20.cs ra xem tên hàm chính xác là gì nhé
                _tiaEngine.ImportBlock(filePath); 

                ConsoleUI.PrintSuccess("Import Completed!");
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"Import Error: {ex.Message}");
            }
            
            Console.WriteLine("Press any key to return...");
            Console.ReadKey();
        }

        // --- LOGIC AI (Giữ nguyên) ---
        static async Task ProcessAI(string userPrompt, string mode)
        {
            // ... (Giữ nguyên nội dung hàm ProcessAI như trước) ...
            // Demo nhanh để code chạy được:
            string category = "PLC Programming";
            string lang = "SCL";
            if(mode == "SCADA") { category="SCADA"; lang="";}
            
            var task = _aiCore.GenerateScriptFromGemini(_aiCore.BuildPlcPrompt(category, "Siemens", "S7-1500", "FB", lang, userPrompt, ""));
            await ConsoleUI.ShowSpinner(task);
            string code = await task;
            
            if(!string.IsNullOrEmpty(code))
            {
                ConsoleUI.PrintSuccess("Done!");
                _lastGeneratedFilePath = _aiCore.SaveScriptToFile(code, category, lang);
            }
            else ConsoleUI.PrintError("Failed.");
            
            Thread.Sleep(1000);
        }
    }
}