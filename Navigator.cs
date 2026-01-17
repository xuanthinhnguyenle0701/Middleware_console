using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms; // Vẫn giữ để dùng Application.StartupPath nếu cần, nhưng không dùng Dialog

namespace Middleware_console
{
    // --- CLASS HỖ TRỢ ĐỌC CATALOG PLC TỪ JSON ---
    public class PlcCatalogItem
    {
        public string Name { get; set; }
        public string OrderNumber { get; set; }
        public string Version { get; set; }

        public string GetTypeIdentifier()
        {
            // Format chuẩn của TIA Portal Openness
            return $"OrderNumber:{OrderNumber}/{Version}";
        }
    }

    // --- ENUM TRẠNG THÁI ỨNG DỤNG ---
    public enum AppState
    {
        MainMenu,
        AI_Menu,
        AI_InputLogic,
        AI_Processing,
        TIA_Menu,
        TIA_Processing,
        Exit
    }

    internal class Navigator
    {
        // CORE ENGINES
        private static GeminiCore _aiCore = new GeminiCore();
        private static TIA_V20 _tiaEngine = new TIA_V20();

        // STATE VARIABLES
        private static string _currentAiMode = "";

        // TIA Session Variables
        private static string _currentProjectName
        {
            get
            {
                // Nếu đang kết nối -> Lấy tên thật từ Engine
                if (_tiaEngine.IsConnected)
                {
                    // Tận dụng thuộc tính ProjectName ta đã viết trong TIA_V20.cs
                    return _tiaEngine.ProjectName;
                }
                // Nếu mất kết nối -> Trả về None
                return "None";
            }
        }
        private static string _selectedDeviceName = "None";
        private static string _selectedDeviceType = "Unknown";
        private static string _selectedArticle = "Unknown";
        private static string _selectedFirmware = "Unknown";
        private static string _selectedDeviceIp = "Unknown";

        [STAThread]
        static async Task Main(string[] args)
        {
            // Config TLS cho AI
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            AppState currentState = AppState.MainMenu;

            while (currentState != AppState.Exit)
            {
                if (currentState != AppState.AI_Processing)
                    Console.Clear();

                switch (currentState)
                {
                    // =================================================================================
                    // MAIN MENU
                    // =================================================================================
                    case AppState.MainMenu:
                        ConsoleUI.PrintHeader("GEMINI AI MIDDLEWARE & TIA AUTOMATION");
                        string choice = ConsoleUI.SelectOption("Select Module:", new[] {
                            "AI Code Generator",
                            "TIA Portal Automation",
                            "Exit"
                        });

                        if (choice.Contains("AI")) currentState = AppState.AI_Menu;
                        else if (choice.Contains("TIA")) currentState = AppState.TIA_Menu;
                        else currentState = AppState.Exit;
                        break;

                    // =================================================================================
                    // AI MODULE
                    // =================================================================================
                    case AppState.AI_Menu:
                        ConsoleUI.PrintHeader("MODULE: AI GENERATOR");
                        string aiChoice = ConsoleUI.SelectOption("Generate type:", new[] {
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
                            if (aiChoice.Contains("SCL")) _currentAiMode = "SCL";
                            else if (aiChoice.Contains("STL")) _currentAiMode = "STL";
                            else if (aiChoice.Contains("FBD")) _currentAiMode = "FBD";
                            else if (aiChoice.Contains("LAD")) _currentAiMode = "LAD";
                            else if (aiChoice.Contains("SCADA")) _currentAiMode = "SCADA";
                            currentState = AppState.AI_InputLogic;
                        }
                        break;

                    case AppState.AI_InputLogic:
                        ConsoleUI.PrintHeader($"INPUT LOGIC FOR: {_currentAiMode}");
                        string userPrompt = ConsoleUI.GetMultiLineInput("Enter requirements");
                        ConsoleUI.PrintStep("Ready to send request to Gemini...");
                        Console.WriteLine("Press [ENTER] to confirm, [ESC] to cancel.");
                        if (Console.ReadKey().Key == ConsoleKey.Escape) currentState = AppState.AI_Menu;
                        else
                        {
                            currentState = AppState.AI_Processing;
                            await _aiCore.ProcessAI(userPrompt, _currentAiMode);
                            currentState = AppState.AI_Menu;
                        }
                        break;

                    // =================================================================================
                    // TIA MODULE - MENU
                    // =================================================================================
                    case AppState.TIA_Menu:
                        ConsoleUI.PrintHeader("TIA AUTOMATION - PROJECT MENU");
                        string tiaStatus = _tiaEngine.IsConnected ? "[CONNECTED]" : "[DISCONNECTED]";
                        Console.WriteLine($"Status: {tiaStatus} | Project: {_currentProjectName}\n");

                        string menuAction = ConsoleUI.SelectOption("Actions:", new[] {
                            "Create New Project",
                            "Open TIA Project",
                            "Connect to Running TIA",
                            "Close TIA Portal",
                            "Go to Operations (Hardware/Software)",
                            "Back to Main Menu"
                        });

                        if (menuAction.Contains("Back")) currentState = AppState.MainMenu;
                        else if (menuAction.Contains("Create New")) CreateProjectLogic();
                        else if (menuAction.Contains("Open TIA")) OpenProjectLogic();
                        else if (menuAction.Contains("Connect")) ConnectTiaLogic();
                        else if (menuAction.Contains("Close"))
                        {
                            _tiaEngine.CloseTIA();
                            ConsoleUI.PrintSuccess("TIA Portal Closed.");
                            Thread.Sleep(1000);
                        }
                        else if (menuAction.Contains("Go to Operations"))
                        {
                            if (_tiaEngine.IsConnected) currentState = AppState.TIA_Processing;
                            else
                            {
                                ConsoleUI.PrintError("You must connect to a project first!");
                                Thread.Sleep(1500);
                            }
                        }
                        break;

                    // =================================================================================
                    // TIA MODULE - PROCESSING
                    // =================================================================================
                    case AppState.TIA_Processing:
                        Console.Clear();
                        ConsoleUI.PrintHeader("TIA OPERATIONS HUB");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"[STATUS]: {(_tiaEngine.IsConnected ? "CONNECTED" : "DISCONNECTED")} | [PROJECT]: {_currentProjectName}\n");
                        Console.WriteLine("----------------------------------------------------------------");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[SELECTED DEVICE]: {_selectedDeviceName}");
                        Console.WriteLine($"[DEVICE TYPE]    : {_selectedDeviceType}");
                        Console.WriteLine($"[ARTICLE NUMBER] : {_selectedArticle}"); 
                        Console.WriteLine($"[FIRMWARE]       : {_selectedFirmware}");
                        Console.WriteLine($"[IP ADDRESS]     : {_selectedDeviceIp}");
                        Console.ResetColor();
                        Console.WriteLine("----------------------------------------------------------------\n");

                        string procAction = ConsoleUI.SelectOption("Select Operation:", new[] {
                            "Create Device (Add new device)",
                            "Choose Device (Select existing device)",
                            "Create FB/FC (Import file)",
                            "Compile (HW/SW)",
                            "Download to Device",
                            "Save Project",
                            "Back to TIA Menu"
                        });

                        if (procAction.Contains("Back")) currentState = AppState.TIA_Menu;
                        else if (procAction.Contains("Create Device")) CreateDeviceLogic();
                        else if (procAction.Contains("Choose Device")) ChooseDeviceLogic();
                        else if (procAction.Contains("Create FB")) CreateBlockLogic();
                        else if (procAction.Contains("Compile")) CompileLogic();
                        else if (procAction.Contains("Download")) ConsoleUI.PrintError("Download feature not implemented yet.");
                        else if (procAction.Contains("Save"))
                        {
                            ConsoleUI.PrintStep("Saving Project...");
                            if (_tiaEngine.SaveProject()) ConsoleUI.PrintSuccess("Saved!");
                            else ConsoleUI.PrintError("Save Failed.");
                            Thread.Sleep(1000);
                        }
                        break;
                }
            }
        }

        static void ConnectTiaLogic()
        {
            ConsoleUI.PrintStep("Connecting to running TIA instance...");
            try
            {
                _tiaEngine.ConnectToTiaPortal();
                if (_tiaEngine.IsConnected)
                {
                    ConsoleUI.PrintSuccess($"Connected to: {_tiaEngine.ProjectName}");
                }
            }
            catch (Exception ex) { ConsoleUI.PrintError(ex.Message); }
            Thread.Sleep(1000);
        }

        // 2. TẠO PROJECT MỚI
        static void CreateProjectLogic()
        {
            ConsoleUI.PrintHeader("CREATE NEW PROJECT");
            Console.WriteLine("Enter the DIRECTORY path where you want to save the project.");
            Console.Write("Path > ");

            string path = Console.ReadLine()?.Trim().Replace("\"", "");

            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                ConsoleUI.PrintError("Invalid directory path!");
                Console.ReadKey();
                return;
            }

            string name = ConsoleUI.GetInput("Enter Project Name:");

            ConsoleUI.PrintStep("Creating TIA Project (This takes time)...");
            if (_tiaEngine.CreateTIAproject(path, name, true))
            {
                ConsoleUI.PrintSuccess($"Project '{_tiaEngine.ProjectName}' Created!");
            }
            else
            {
                ConsoleUI.PrintError("Failed to create project.");
                Console.ReadKey();
            }
            Thread.Sleep(1000);
        }

        // 3. MỞ PROJECT CÓ SẴN
        static void OpenProjectLogic()
        {
            ConsoleUI.PrintHeader("OPEN TIA PROJECT");
            Console.WriteLine("Option 1: Paste full path to .ap* file.");
            Console.WriteLine("Option 2: Press ENTER to scan current directory.");
            Console.Write("Path > ");

            string path = Console.ReadLine()?.Trim().Replace("\"", "");

            if (string.IsNullOrEmpty(path))
            {
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                Console.WriteLine($"\nScanning in: {currentDir}");

                var files = Directory.GetFiles(currentDir, "*.ap*");

                if (files.Length == 0)
                {
                    ConsoleUI.PrintError("No TIA Project files found.");
                    Console.ReadKey();
                    return;
                }

                string selectedFile = ConsoleUI.SelectOption("Detected Projects:", files.Select(Path.GetFileName).ToArray());
                path = Path.Combine(currentDir, selectedFile);
            }

            if (!File.Exists(path))
            {
                ConsoleUI.PrintError($"File not found: {path}");
                Console.ReadKey();
                return;
            }

            ConsoleUI.PrintStep($"Opening: {Path.GetFileName(path)}...");
            if (_tiaEngine.CreateTIAproject(path, "", false))
            {
                ConsoleUI.PrintSuccess($"Project '{_tiaEngine.ProjectName}' Opened!");
            }
            else ConsoleUI.PrintError("Failed to open project.");

            Thread.Sleep(1000);
        }

        // --- LOGIC TẠO THIẾT BỊ MỚI (ĐÃ SỬA THEO QUY TRÌNH 5 BƯỚC) ---
        static void CreateDeviceLogic()
        {
            // 1. Load Catalog (Load 1 lần đầu)
            var catalog = _tiaEngine.LoadPlcCatalog();

            if (catalog == null || catalog.Count == 0)
            {
                ConsoleUI.PrintError("Catalog empty or PLCCatalog.json missing/invalid.");
                Console.ReadKey();
                return;
            }

            // --- BIẾN TRẠNG THÁI (State Variables) ---
            // Khai báo ngoài vòng lặp để giữ giá trị khi Back/Next
            int step = 1;
            PlcFamilyModel selectedFamilyObj = null;
            PlcDeviceModel selectedDeviceObj = null;
            PlcVariantModel selectedVariantObj = null;
            string selectedVersion = "";
            string devName = "";
            string ipAddr = "";

            // --- VÒNG LẶP ĐIỀU HƯỚNG ---
            while (step > 0)
            {
                Console.Clear();
                ConsoleUI.PrintHeader($"CREATE NEW DEVICE - STEP {step}/6");
                Console.WriteLine("| Esc to go back | Enter to confirm |\n");
                
                // Hiển thị Breadcrumb (Dấu vết) để user biết mình đang chọn cái gì
                if (step > 1) Console.WriteLine($"[Family]: {selectedFamilyObj?.Family}");
                if (step > 2) Console.WriteLine($"[Model ]: {selectedDeviceObj?.Name}");
                if (step > 3) Console.WriteLine($"[Order ]: {selectedVariantObj?.OrderNumber}");
                if (step > 4) Console.WriteLine($"[Ver   ]: {selectedVersion}");
                if (step > 5) Console.WriteLine($"[Name  ]: {devName}");
                Console.WriteLine("----------------------------------------------------\n");

                switch (step)
                {
                    // --- BƯỚC 1: CHỌN FAMILY ---
                    case 1:
                        var families = catalog
                            .Where(x => !string.IsNullOrEmpty(x.Family))
                            .Select(x => x.Family)
                            .ToArray();

                        string familyChoice = ConsoleUI.SelectOption("Step 1/6: Select PLC Family", families, true);
                        
                        // Kiểm tra nếu User ấn ESC (null)
                        if (familyChoice == null) return; // Thoát khỏi hàm

                        selectedFamilyObj = catalog.First(f => f.Family == familyChoice);
                        step++; // Tiến lên bước 2
                        break;

                    // --- BƯỚC 2: CHỌN TÊN THIẾT BỊ ---
                    case 2:
                        if (selectedFamilyObj.Devices == null || selectedFamilyObj.Devices.Count == 0)
                        {
                            ConsoleUI.PrintError("No devices in this family.");
                            Console.ReadKey();
                            step--; // Tự động lùi
                            break;
                        }

                        var deviceNames = selectedFamilyObj.Devices.Select(d => d.Name).ToArray();
                        string deviceChoice = ConsoleUI.SelectOption("Step 2/6: Select CPU Model", deviceNames, true);

                        if (deviceChoice == null) { step--; break; } // Quay lại bước 1

                        selectedDeviceObj = selectedFamilyObj.Devices.First(d => d.Name == deviceChoice);
                        step++;
                        break;

                    // --- BƯỚC 3: CHỌN MÃ HÀNG ---
                    case 3:
                        if (selectedDeviceObj.Variants == null || selectedDeviceObj.Variants.Count == 0)
                        {
                            ConsoleUI.PrintError("No Order Numbers listed.");
                            Console.ReadKey();
                            step--; 
                            break;
                        }

                        var orderNumbers = selectedDeviceObj.Variants.Select(v => v.OrderNumber).ToArray();
                        string orderChoice = ConsoleUI.SelectOption("Step 3/6: Select Article Number", orderNumbers, true);

                        if (orderChoice == null) { step--; break; } // Quay lại bước 2

                        selectedVariantObj = selectedDeviceObj.Variants.First(v => v.OrderNumber == orderChoice);
                        step++;
                        break;

                    // --- BƯỚC 4: CHỌN FIRMWARE ---
                    case 4:
                        if (selectedVariantObj.Versions == null || selectedVariantObj.Versions.Count == 0)
                        {
                            ConsoleUI.PrintError("No firmware versions defined.");
                            Console.ReadKey();
                            step--;
                            break;
                        }

                        string verChoice = ConsoleUI.SelectOption("Step 4/6: Select Firmware Version", selectedVariantObj.Versions.ToArray(), true);

                        if (verChoice == null) { step--; break; } // Quay lại bước 3

                        selectedVersion = verChoice;
                        step++;
                        break;

                    // --- BƯỚC 5: NHẬP TÊN ---
                    case 5:
                        string inputName = ConsoleUI.GetInput($"Enter Name (Default: PLC_1):", true, true);
                        
                        if (inputName == null) { step--; break; } // Quay lại bước 4

                        devName = string.IsNullOrWhiteSpace(inputName) ? "PLC_1" : inputName;
                        step++;
                        break;

                    // --- BƯỚC 6: NHẬP IP VÀ TẠO ---
                    case 6:
                        string inputIp = ConsoleUI.GetInput($"Enter IP (Default: 192.168.0.1):", true, true);

                        if (inputIp == null) { step--; break; } // Quay lại bước 5

                        ipAddr = string.IsNullOrWhiteSpace(inputIp) ? "192.168.0.1" : inputIp;

                        // THỰC THI
                        ConsoleUI.PrintStep("Creating Device... Please wait...");
                        try
                        {
                            string cleanOrder = selectedVariantObj.OrderNumber.Replace(" ", "");
                            string typeId = $"OrderNumber:{cleanOrder}/{selectedVersion}";

                            _tiaEngine.CreateDev(devName, typeId, ipAddr, "");

                            _selectedDeviceName = devName;
                            _selectedDeviceType = selectedDeviceObj.Name;
                            _selectedArticle    = selectedVariantObj.OrderNumber;
                            _selectedFirmware   = selectedVersion;
                            _selectedDeviceIp   = ipAddr;

                            ConsoleUI.PrintSuccess($"Device '{devName}' Created Successfully!");
                            Console.WriteLine("Press any key to continue...");
                            Console.ReadKey();
                            step = 0; // Thoát vòng lặp (Hoàn thành)
                        }
                        catch (Exception ex)
                        {
                            ConsoleUI.PrintError("Error: " + ex.Message);
                            Console.WriteLine("Press any key to try again or ESC to go back...");
                            if (Console.ReadKey().Key == ConsoleKey.Escape) step--; // Cho phép quay lại sửa IP
                        }
                        break;
                }
            }
        }

        static void ChooseDeviceLogic()
        {
            // Gọi hàm GetPlcList mới
            var devices = _tiaEngine.GetDeviceList();

            if (devices.Count == 0)
            {
                ConsoleUI.PrintError("No PLC found in project.");
                Console.ReadKey();
                return;
            }

            // Hiển thị menu đẹp hơn
            var options = devices.Select(x => x.ShowMenu).ToArray();
            string choice = ConsoleUI.SelectOption("Choose a Device:", options);

            // Tìm lại object gốc
            var selectedInfo = devices.FirstOrDefault(x => x.ShowMenu == choice);

            if (selectedInfo != null)
            {
                _selectedDeviceName = selectedInfo.Name;
                _selectedDeviceType = selectedInfo.CpuType;
                _selectedDeviceIp = selectedInfo.IpAddress;
                _selectedArticle = selectedInfo.ArticleNumber;
                _selectedFirmware = selectedInfo.Version;

                ConsoleUI.PrintSuccess($"Selected: {_selectedDeviceName}");
            }
            Thread.Sleep(1000);
        }

        // --- CẬP NHẬT 3: NHẬP SOURCE FILE ---
        static void CreateBlockLogic()
        {
            ConsoleUI.PrintHeader("IMPORT SCL/AWL SOURCE");
            Console.WriteLine("Enter full path to the source file (.scl, .awl)");
            Console.Write("Path > ");

            string path = Console.ReadLine()?.Trim().Replace("\"", "");

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ConsoleUI.PrintError("File not found!");
                Console.ReadKey();
                return;
            }

            ConsoleUI.PrintStep($"Importing {Path.GetFileName(path)}...");
            try
            {
                _tiaEngine.ImportBlock(path);
                ConsoleUI.PrintSuccess("Block Created!");
            }
            catch (Exception ex) { ConsoleUI.PrintError("Import Failed: " + ex.Message); }
            Console.ReadKey();
        }

        static void CompileLogic()
        {
            if (_selectedDeviceName == "None")
            {
                ConsoleUI.PrintError("Please Choose Device first!");
                Console.ReadKey();
                return;
            }

            string mode = ConsoleUI.SelectOption("Compile Mode:", new[] { "Hardware", "Software", "Both" });
            bool hw = mode.Contains("Hardware") || mode.Contains("Both");
            bool sw = mode.Contains("Software") || mode.Contains("Both");

            ConsoleUI.PrintStep("Compiling...");
            string result = _tiaEngine.CompileSpecific(_selectedDeviceName, hw, sw);
            Console.WriteLine(result);
            Console.ReadKey();
        }

    }
}