using System;
using System.Threading;
using System.Threading.Tasks;

namespace Middleware_console
{
    public enum AppState
    {
        MainMenu,
        AI_Menu,
        AI_InputLogic,
        AI_Processing,
        Exit
    }

    internal class Navigator
    {
        private static GeminiCore _aiCore = new GeminiCore();
        private static string _currentMode = ""; 

        [STAThread]
        static async Task Main(string[] args)
        {
            // BẮT BUỘC: Cấu hình TLS 1.2
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            AppState currentState = AppState.MainMenu;

            while (currentState != AppState.Exit)
            {
                if (currentState != AppState.AI_Processing)
                    Console.Clear();

                switch (currentState)
                {
                    // --- MENU CHÍNH ---
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
                            ConsoleUI.PrintStep("TIA Module is temporarily disabled.");
                            Console.ReadKey();
                        }
                        else currentState = AppState.Exit;
                        break;

                    // --- MENU AI ---
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

                        if (aiChoice.Contains("Back")) currentState = AppState.MainMenu;
                        else
                        {
                            if (aiChoice.Contains("SCL")) _currentMode = "SCL";
                            else if (aiChoice.Contains("STL")) _currentMode = "STL";
                            else if (aiChoice.Contains("FBD")) _currentMode = "FBD";
                            else if (aiChoice.Contains("LAD")) _currentMode = "LAD";
                            else if (aiChoice.Contains("SCADA")) _currentMode = "SCADA";

                            currentState = AppState.AI_InputLogic;
                        }
                        break;

                    // --- NHẬP INPUT ---
                    case AppState.AI_InputLogic:
                        ConsoleUI.PrintHeader($"INPUT LOGIC FOR: {_currentMode}");
                        string userPrompt = ConsoleUI.GetMultiLineInput("Enter requirements");

                        ConsoleUI.PrintStep("Ready to send request to Gemini...");
                        Console.WriteLine("Press [ENTER] to confirm, [ESC] to cancel.");

                        if (Console.ReadKey().Key == ConsoleKey.Escape)
                            currentState = AppState.AI_Menu;
                        else
                        {
                            currentState = AppState.AI_Processing;
                            
                            // GỌI HÀM XỬ LÝ ĐÃ CHUYỂN VÀO GEMINICORE
                            await _aiCore.ProcessAI(userPrompt, _currentMode);
                            
                            currentState = AppState.AI_Menu;
                        }
                        break;
                }
            }
            ConsoleUI.PrintStep("Goodbye!");
            Thread.Sleep(1000);
        }
    }
}