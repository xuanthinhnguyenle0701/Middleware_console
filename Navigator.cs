using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

        // Nhóm AI
        AI_Menu,
        AI_InputLogic,
        AI_Processing,

        // Nhóm TIA Automation
        TIA_Menu,        // Menu quản lý (Connect/Open/Create Project)
        TIA_Processing,  // Menu thao tác (Create Dev/Compile/Download)

        Exit
    }

    internal class Navigator
    {
        // Khởi tạo các Engine
        private static GeminiCore _aiCore = new GeminiCore();
        private static TIA_V20 _tiaEngine = new TIA_V20();
        private static SatEngine _satEngine = new SatEngine();
   

        // Biến lưu trạng thái hiển thị (Status Labeling)
        private static string _currentProjectName = "None";
        private static string _currentDeviceName = "None";
        private static string _currentDeviceType = "None";
        private static string _currentIp = "0.0.0.0";

        // Biến hỗ trợ AI
        private static string _lastGeneratedFilePath = "";
        private static string _currentMode = "";

        static async Task Main(string[] args)
        {
            // Cấu hình bắt buộc cho TIA Openness và Web Request
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            AppState currentState = AppState.MainMenu;

            while (currentState != AppState.Exit)
            {
                // Chỉ xóa màn hình khi không phải đang xử lý (để giữ log chạy)
                if (currentState != AppState.AI_Processing)
                    Console.Clear();

                switch (currentState)
                {
                    // =========================================================
                    // 1. MAIN MENU
                    // =========================================================
                    case AppState.MainMenu:
                        ConsoleUI.PrintHeader("GEMINI AI MIDDLEWARE");
                        string mainChoice = ConsoleUI.SelectOption("Select Module:", new[] {
                            "1. AI Code Generator",
                            "2. TIA Portal Automation",
                            "3. Exit"
                        });

                        if (mainChoice.Contains("1")) currentState = AppState.AI_Menu;
                        else if (mainChoice.Contains("2")) currentState = AppState.TIA_Menu;
                        else currentState = AppState.Exit;
                        break;

                    // =========================================================
                    // 2. AI MENU (GIỮ NGUYÊN)
                    // =========================================================
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
                        currentState = AppState.AI_Processing;
                        await ProcessAI(userPrompt, _currentMode);
                        currentState = AppState.AI_Menu;
                        break;

                    // =========================================================
                    // 3. TIA MENU (STATE 1: QUẢN LÝ DỰ ÁN)
                    // =========================================================
                    case AppState.TIA_Menu:
                        ConsoleUI.PrintHeader("TIA AUTOMATION - PROJECT MANAGER");
                        string tiaMenuChoice = ConsoleUI.SelectOption("Select Action:", new[] {
                            "1. Create new project",
                            "2. Open TIA project",
                            "3. Connect to TIA (Running)",
                            "4. Close TIA",
                            "5. Back to Main Menu"
                        });

                        if (tiaMenuChoice.Contains("Back"))
                        {
                            currentState = AppState.MainMenu;
                        }
                        else if (tiaMenuChoice.Contains("1. Create"))
                        {
                            Console.Write("Enter Folder Path (e.g D:\\TIA): ");
                            string path = Console.ReadLine();
                            Console.Write("Enter Project Name: ");
                            string name = Console.ReadLine();
                            
                            ConsoleUI.PrintStep("Creating Project...");
                            if (_tiaEngine.CreateTIAproject(path, name, true))
                            {
                                _currentProjectName = name;
                                ConsoleUI.PrintSuccess("Project Created!");
                                currentState = AppState.TIA_Processing;
                            }
                            else ConsoleUI.PrintError("Failed to create project.");
                            Console.ReadKey();
                        }
                        else if (tiaMenuChoice.Contains("2. Open"))
                        {
                            Console.Write("Enter full path to .ap1x file: ");
                            string path = Console.ReadLine().Replace("\"", ""); // Xóa ngoặc kép nếu user copy path
                            
                            ConsoleUI.PrintStep("Opening Project...");
                            if (_tiaEngine.CreateTIAproject(path, "", false))
                            {
                                _currentProjectName = Path.GetFileNameWithoutExtension(path);
                                ConsoleUI.PrintSuccess("Project Opened!");
                                currentState = AppState.TIA_Processing;
                            }
                            else ConsoleUI.PrintError("Failed to open project.");
                            Console.ReadKey();
                        }
                        else if (tiaMenuChoice.Contains("3. Connect"))
                        {
                            ConsoleUI.PrintStep("Connecting...");
                            if (_tiaEngine.ConnectToTIA())
                            {
                                // GỌI HÀM MỚI ĐỂ LẤY TÊN THẬT
                                _currentProjectName = _tiaEngine.GetProjectName(); 
                                
                                ConsoleUI.PrintSuccess($"Connected to project: {_currentProjectName}");
                                currentState = AppState.TIA_Processing;
                            }
                            else ConsoleUI.PrintError("No running TIA Portal found.");
                            Console.ReadKey();
                        }
                        else if (tiaMenuChoice.Contains("4. Close"))
                        {
                            _tiaEngine.CloseTIA();
                            _currentProjectName = "None";
                            ConsoleUI.PrintSuccess("TIA Closed.");
                            Thread.Sleep(1000);
                        }
                        break;

                    // =========================================================
                    // 4. TIA PROCESSING (STATE 2: THAO TÁC TRONG DỰ ÁN)
                    // =========================================================
                    case AppState.TIA_Processing:
                        // --- HEADER TRẠNG THÁI (STATUS LABELING) ---
                        Console.Clear();
                        string connStatus = _tiaEngine.IsConnected ? "CONNECTED" : "DISCONNECTED";
                        
                        // In Status Bar màu xanh Cyan
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("=============================================================");
                        Console.WriteLine($"[TIA: {connStatus}]   [PROJECT: {_currentProjectName}]");
                        Console.WriteLine($"[DEVICE: {_currentDeviceName}]   [TYPE: {_currentDeviceType}]   [IP: {_currentIp}]");
                        Console.WriteLine("=============================================================");
                        Console.ResetColor();
                        Console.WriteLine(); // Xuống dòng

                        string procChoice = ConsoleUI.SelectOption("Project Operations:", new[] {
                            "1. Back to TIA Menu",
                            "2. Create Device",
                            "3. Choose Device",
                            "4. Create FB (Import SCL)",
                            "5. Create FC",
                            "6. Create Faceplate",
                            "7. Compile",
                            "8. Download to device",
                            "9. Save Project",
                            "10. Run PLC",
                            "11. Stop PLC",
                            "12. CHECK CONNECTION (Test Online)",
                            "13. Update Firmware"
                            
                        });

                        if (procChoice.Contains("1. Back"))
                        {
                            currentState = AppState.TIA_Menu;
                        }
                        else if (procChoice.Contains("2. Create Device"))
                        {
                            HandleCreateDevice();
                        }
                        else if (procChoice.Contains("3. Choose Device"))
                        {
                            var devices = _tiaEngine.GetPlcList();
                            if (devices.Count == 0) ConsoleUI.PrintError("No devices found in project.");
                            else
                            {
                                string selected = ConsoleUI.SelectOption("Available Devices:", devices.ToArray());
                                
                                // Cập nhật Tên
                                _currentDeviceName = selected;
                                
                                // --- CẬP NHẬT MỚI: TỰ ĐỘNG LẤY TYPE VÀ IP ---
                                ConsoleUI.PrintStep($"Fetching details for {selected}...");
                                
                                try
                                {
                                    // Gọi 2 hàm mới vừa viết bên TIA_V20
                                    _currentDeviceType = _tiaEngine.GetDeviceType(selected);
                                    _currentIp = _tiaEngine.GetDeviceIp(selected);
                                    
                                    ConsoleUI.PrintSuccess($"Selected: {selected}");
                                    ConsoleUI.PrintInfo($"Type: {_currentDeviceType}");
                                    ConsoleUI.PrintInfo($"IP:   {_currentIp}");
                                }
                                catch (Exception ex)
                                {
                                    ConsoleUI.PrintError($"Warning: Could not fetch details. {ex.Message}");
                                }
                            }
                        }
                        else if (procChoice.Contains("4. Create FB"))
                        {
                            TiaImportLogic("FB");
                        }
                        else if (procChoice.Contains("5. Create FC"))
                        {
                            TiaImportLogic("FC");
                        }
                        else if (procChoice.Contains("6. Create Faceplate"))
                        {
                            ConsoleUI.PrintStep("Faceplate feature coming soon...");
                            Thread.Sleep(1000);
                        }
                        else if (procChoice.Contains("7. Compile"))
                        {
                            string compileType = ConsoleUI.SelectOption("Compile Mode:", new[] { "Hardware", "Software", "Both" });
                            bool hw = compileType == "Hardware" || compileType == "Both";
                            bool sw = compileType == "Software" || compileType == "Both";

                            ConsoleUI.PrintStep($"Compiling {_currentDeviceName}...");
                            string result = _tiaEngine.CompileSpecific(_currentDeviceName, hw, sw);
                            Console.WriteLine(result);
                            Console.WriteLine("\nPress any key to return to menu...");
                            Console.ReadKey();
                        }
                        else if (procChoice.Contains("8. Download"))
                        {
                            var adapters = TIA_V20.GetSystemNetworkAdapters();
                            if (adapters.Count == 0) ConsoleUI.PrintError("No Network Interface found.");
                            else
                            {
                                string netCard = ConsoleUI.SelectOption("Select PG/PC Interface:", adapters.ToArray());
                                ConsoleUI.PrintStep($"Downloading to {_currentIp} via {netCard}...");
                                
                                string result = _tiaEngine.DownloadToPLC(_currentDeviceName, _currentIp, netCard);
                                Console.WriteLine(result);
                            }
                            Console.WriteLine("\nPress any key to return to menu...");
                            Console.ReadKey();
                        }
                        else if (procChoice.Contains("9. Save"))
                        {
                            if (_tiaEngine.SaveProject()) ConsoleUI.PrintSuccess("Project Saved.");
                            else ConsoleUI.PrintError("Save failed.");
                            Thread.Sleep(1000);
                            Console.WriteLine("\nPress any key to return to menu...");
                            Console.ReadKey();
                        }
                        
                    else if (procChoice.Contains("10.")) // MENU: 10. RUN PLC
                    {
                        Console.WriteLine("\n--- MANUAL START PLC (Via Download) ---");
                        var adapters = TIA_V20.GetSystemNetworkAdapters();
                        string netCard = ConsoleUI.SelectOption("Select Network Adapter:", adapters.ToArray());

                        Console.WriteLine(">> Processing Start Command...");
                        // Gọi hàm cũ của TIA
                        string msg = _tiaEngine.ChangePlcState(_currentDeviceName, _currentIp, netCard, true);
                        
                        // IN KẾT QUẢ RA MÀN HÌNH (Fix lỗi cũ của bạn)
                        ConsoleUI.PrintResult(msg); // Hoặc Console.WriteLine(msg);
                        
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey();
                    }
                    else if (procChoice.Contains("11.")) // MENU: 11. STOP PLC
                    {
                        Console.WriteLine("\n--- MANUAL STOP PLC (Via Download) ---");
                        var adapters = TIA_V20.GetSystemNetworkAdapters();
                        string netCard = ConsoleUI.SelectOption("Select Network Adapter:", adapters.ToArray());

                        Console.WriteLine(">> Processing Stop Command...");
                        // Gọi hàm cũ của TIA
                        string msg = _tiaEngine.ChangePlcState(_currentDeviceName, _currentIp, netCard, false);
                        
                        ConsoleUI.PrintResult(msg);
                        
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey();
                    }
                    else if (procChoice.Contains("12.")) // MENU: 12. CHECK CONNECTION
                    {
                        Console.WriteLine("\n--- CHECK PLC CONNECTION ---");
                        var adapters = TIA_V20.GetSystemNetworkAdapters();
                        string netCard = ConsoleUI.SelectOption("Select Network Adapter:", adapters.ToArray());

                        // Gọi hàm kiểm tra kết nối (Thay thế cho Flash LED)
                        string msg = _tiaEngine.GetPlcStatus(_currentDeviceName, netCard);
                        
                        ConsoleUI.PrintResult(msg);
                        
                        Console.WriteLine("Press any key to continue...");
                        Console.ReadKey();
                    }
                    else if (procChoice.Contains("13.")) // MENU: 11. FIRMWARE UPDATE
                    {
                        Console.WriteLine("\n--- PLC FIRMWARE UPDATE (NATIVE) ---");
                        Console.WriteLine("WARNING: PLC will STOP during this process.");

                        // 1. Chọn Card mạng (Mượn hàm của TIA cho nhanh)
                        var adapters = TIA_V20.GetSystemNetworkAdapters();
                        string netCard = ConsoleUI.SelectOption("Select Network Adapter:", adapters.ToArray());

                        // 2. Xác nhận an toàn
                        Console.WriteLine();
                        Console.BackgroundColor = ConsoleColor.DarkRed;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($" ARE YOU SURE YOU WANT TO UPDATE FW FOR IP: {_currentIp}? ");
                        Console.ResetColor();
                        Console.Write("Type 'YES' to continue: ");
                        
                        if (Console.ReadLine() == "YES")
                        {
                            // 3. Gọi SatEngine (Bản Dynamic)
                            _satEngine.ExecuteFirmwareUpdate(_currentIp, netCard);
                        }
                        else
                        {
                            Console.WriteLine("Operation Cancelled.");
                        }

                        Console.WriteLine("\nPress any key to continue...");
                        Console.ReadKey();
                    }
                        break;
                }
            }
        }

        // --- LOGIC: CREATE DEVICE (JSON + MANUAL) ---
        static void HandleCreateDevice()
        {
            string typeIdentifier = "";
            string inputMode = ConsoleUI.SelectOption("Select Device Input Mode:", new[] {
                "1. Load from Catalog (JSON)",
                "2. Manual Input (Order Number)"
            });

            if (inputMode.Contains("1. Load"))
            {
                try 
                {
                    string jsonPath = "PlcCatalog.json";
                    if (File.Exists(jsonPath))
                    {
                        string jsonContent = File.ReadAllText(jsonPath);
                        var catalog = JsonConvert.DeserializeObject<List<PlcCatalogItem>>(jsonContent);

                        // --- SỬA LỖI Ở ĐÂY: LỌC BỎ DATA RÁC ---
                        if (catalog != null && catalog.Count > 0)
                        {
                            // Chỉ lấy những dòng có Tên và Mã đầy đủ (Khắc phục lỗi Value cannot be null)
                            var validItems = catalog.Where(x => !string.IsNullOrEmpty(x.Name) && !string.IsNullOrEmpty(x.OrderNumber)).ToList();

                            if (validItems.Count > 0)
                            {
                                // Tạo menu từ danh sách đã lọc sạch
                                var options = validItems.Select(x => $"{x.Name} ({x.OrderNumber})").ToArray();
                                string selectedStr = ConsoleUI.SelectOption("Select PLC Model:", options);
                                
                                // Tìm kiếm an toàn bằng OrderNumber (chắc chắn không null)
                                var selectedItem = validItems.FirstOrDefault(x => selectedStr.Contains(x.OrderNumber));
                                
                                if (selectedItem != null)
                                {
                                    typeIdentifier = selectedItem.GetTypeIdentifier();
                                    ConsoleUI.PrintSuccess($"Selected: {selectedItem.Name} - {selectedItem.Version}");
                                }
                            }
                            else ConsoleUI.PrintError("JSON loaded but all items are invalid (missing Name). Check JSON file.");
                        }
                        else ConsoleUI.PrintError("Catalog JSON is empty.");
                    }
                    else ConsoleUI.PrintError("PlcCatalog.json not found!");
                }
                catch (Exception ex) { ConsoleUI.PrintError($"JSON Error: {ex.Message}"); }
            }
            
            // ... (Phần nhập tay phía dưới giữ nguyên) ...
            if (string.IsNullOrEmpty(typeIdentifier))
            {
                Console.WriteLine("\n--- MANUAL INPUT ---");
                Console.Write("Enter Order Number (e.g. 6ES7 511-1AK02-0AB0): ");
                string orderNum = Console.ReadLine();
                Console.Write("Enter Version (e.g. V4.4): ");
                string ver = Console.ReadLine();
                typeIdentifier = $"OrderNumber:{orderNum}/{ver}";
            }

            Console.Write("Set Device Name: ");
            string devName = Console.ReadLine();
            Console.Write("Set IP Address: ");
            string ip = Console.ReadLine();

            try 
            {
                ConsoleUI.PrintStep($"Creating device...");
                _tiaEngine.CreateDev(devName, typeIdentifier, ip, "");
                ConsoleUI.PrintSuccess($"Device {devName} created successfully.");
                
                // Cập nhật Header
                _currentDeviceName = devName;
                _currentDeviceType = typeIdentifier; 
                _currentIp = ip;
            }
            catch (Exception ex) { ConsoleUI.PrintError($"Create Failed: {ex.Message}"); }
            
            Console.WriteLine("Press any key to return...");
            Console.ReadKey();
        }

        // --- LOGIC: IMPORT SCL ---
        static void TiaImportLogic(string blockType)
        {
            Console.WriteLine($"--- CREATE {blockType} ---");
            string path = "";
            
            if (!string.IsNullOrEmpty(_lastGeneratedFilePath))
            {
                string choice = ConsoleUI.SelectOption($"Use recently generated AI file ({Path.GetFileName(_lastGeneratedFilePath)})?", new[]{"Yes", "No"});
                if (choice == "Yes") path = _lastGeneratedFilePath;
            }

            if (string.IsNullOrEmpty(path))
            {
                Console.Write("Enter path to .scl file: ");
                path = Console.ReadLine().Replace("\"", "");
            }

            if (File.Exists(path))
            {
                try
                {
                    _tiaEngine.CreateFBblockFromSource(path);
                    ConsoleUI.PrintSuccess($"Imported {blockType} successfully!");
                }
                catch (Exception ex) { ConsoleUI.PrintError(ex.Message); }
            }
            else ConsoleUI.PrintError("File not found.");
            
            Console.ReadKey();
        }

        // --- AI LOGIC (GIỮ NGUYÊN) ---
        static async Task ProcessAI(string userPrompt, string mode)
        {
            string category = "PLC Programming";
            string lang = "SCL";
            string blockType = "FB";

            if (mode == "SCADA") { category = "SCADA Designing"; lang = ""; }
            
            var task = _aiCore.GenerateScriptFromGemini(_aiCore.BuildPlcPrompt(category, "Siemens", "S7-1500", blockType, lang, userPrompt, ""));
            await ConsoleUI.ShowSpinner(task);
            string code = await task;

            if (!string.IsNullOrEmpty(code))
            {
                ConsoleUI.PrintSuccess("Code Generated!");
                _lastGeneratedFilePath = _aiCore.SaveScriptToFile(code, category, lang);
            }
            else ConsoleUI.PrintError("AI Failed.");
            Thread.Sleep(1000);
        }
    }
}