using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        // Trạng thái thoát
        Exit
    }

    internal class Navigator
    {
        private static GeminiCore _aiCore = new GeminiCore();

        // Biến lưu dữ liệu tạm
        private static string _lastGeneratedFilePath = "";
        private static string _currentMode = ""; // Lưu lại mode đang chọn (SCL, STL, JSON...)

        [STAThread]
        static async Task Main(string[] args)
        {
            // BẮT BUỘC: Cấu hình TLS 1.2 cho .NET Framework 4.8 để gọi được Google API
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            AppState currentState = AppState.MainMenu;

            // Vòng lặp chính
            while (currentState != AppState.Exit)
            {
                // Xóa màn hình cho sạch (trừ lúc đang xử lý AI thì giữ nguyên để hiện Spinner)
                if (currentState != AppState.AI_Processing)
                    Console.Clear();

                switch (currentState)
                {
                    // --- TRẠNG THÁI 1: MENU CHÍNH ---
                    case AppState.MainMenu:
                        ConsoleUI.PrintHeader("GEMINI AI CODE GENERATOR (STANDALONE)");
                        string choice = ConsoleUI.SelectOption("Select Module:", new[] {
                            "AI Code Generator (Gemini)",
                            "TIA Portal Automation",
                            "Exit"
                        });

                        if (choice.Contains("AI")) currentState = AppState.AI_Menu;
                        else if (choice.Contains("TIA"))
                        {
                            ConsoleUI.PrintStep("TIA Module is temporarily disabled in this version.");
                            Console.WriteLine("Press any key to return...");
                            Console.ReadKey();
                        }
                        else currentState = AppState.Exit;
                        break;

                    // --- TRẠNG THÁI 2: MENU AI ---
                    case AppState.AI_Menu:
                        ConsoleUI.PrintHeader("MODULE: AI GENERATOR");
                        string aiChoice = ConsoleUI.SelectOption("What do you want to generate?", new[] {
                            "SCL - Function Block (.scl)",
                            "STL - Statement List (.awl)",
                            "FBD - Function Block Diagram (.txt)",
                            "LAD - Ladder (.txt)",
                            "SCADA Layout (WinCC Unified JSON)",
                            "Back to Main Menu"
                        });

                        if (aiChoice.Contains("Back"))
                        {
                            currentState = AppState.MainMenu;
                        }
                        else
                        {
                            // Lưu lại lựa chọn để lát nữa dùng build prompt
                            if (aiChoice.Contains("SCL")) _currentMode = "SCL";
                            else if (aiChoice.Contains("STL")) _currentMode = "STL";
                            else if (aiChoice.Contains("FBD")) _currentMode = "FBD";
                            else if (aiChoice.Contains("LAD")) _currentMode = "LAD";
                            else if (aiChoice.Contains("SCADA")) _currentMode = "SCADA";

                            currentState = AppState.AI_InputLogic; // Chuyển sang bước nhập liệu
                        }
                        break;

                    // --- TRẠNG THÁI 3: NHẬP INPUT ---
                    case AppState.AI_InputLogic:
                        ConsoleUI.PrintHeader($"INPUT LOGIC FOR: {_currentMode}");
                        ConsoleUI.PrintStep("Describe your control requirements in detail.");

                        string userPrompt = ConsoleUI.GetMultiLineInput("Enter requirements");

                        // Sau khi nhập xong, chuyển sang trạng thái xử lý
                        // (Ta truyền prompt qua biến cục bộ hoặc gọi hàm xử lý ngay tại state tiếp theo)
                        ConsoleUI.PrintStep("Ready to send request to Gemini...");
                        Console.WriteLine("Press [ENTER] to confirm, [ESC] to cancel.");

                        if (Console.ReadKey().Key == ConsoleKey.Escape)
                            currentState = AppState.AI_Menu;
                        else
                        {
                            currentState = AppState.AI_Processing;
                            await ProcessAI(userPrompt, _currentMode); // Gọi hàm xử lý
                            // Sau khi xử lý xong, quay về Menu AI
                            currentState = AppState.AI_Menu;
                        }
                        break;
                }
            }

            ConsoleUI.PrintStep("Goodbye!");
            Thread.Sleep(1000);
        }

        // --- HÀM XỬ LÝ AI RIÊNG BIỆT ---
        static async Task ProcessAI(string userPrompt, string mode)
        {
            // 1. Chuẩn bị tham số cho Prompt Builder
            string category = "PLC Programming";
            string lang = "SCL";
            string blockType = "FB";

            // Logic map mode sang tham số của GeminiCore
            if (mode == "SCADA")
            {
                category = "SCADA Designing";
                lang = ""; // Không quan trọng với SCADA
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
            // Mặc định là SCL

            // 2. Build Prompt từ Template
            string fullPrompt = _aiCore.BuildPlcPrompt(category, "Siemens", "S7-1500", blockType, lang, userPrompt, "");

            // 3. Gọi AI & Hiển thị Spinner (ConsoleUI)
            var task = _aiCore.GenerateScriptFromGemini(fullPrompt);
            await ConsoleUI.ShowSpinner(task); // Chờ ở đây

            // 4. Lấy kết quả
            string code = await task;

            if (!string.IsNullOrEmpty(code))
            {
                ConsoleUI.PrintSuccess("Code generated successfully!");

                // 5. Lưu file
                _lastGeneratedFilePath = _aiCore.SaveScriptToFile(code, category, lang);

                if (!string.IsNullOrEmpty(_lastGeneratedFilePath))
                {
                    // Tùy chọn mở file nhanh
                    string openChoice = ConsoleUI.SelectOption("File saved. What next?", new[] {
                        "Open File now",
                        "Continue"
                    });

                    if (openChoice.Contains("Open"))
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("explorer.exe", _lastGeneratedFilePath);
                        }
                        catch { ConsoleUI.PrintError("Could not open file."); }
                    }
                }
            }
            else
            {
                ConsoleUI.PrintError("AI failed to generate code (Empty response or Error).");
                Thread.Sleep(2000); // Dừng một chút cho user đọc lỗi
            }
        }
    }
}
