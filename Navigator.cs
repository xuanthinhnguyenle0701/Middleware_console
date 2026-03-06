using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        [STAThread]
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
                            Console.WriteLine("\nOpening File Dialog... (Check Taskbar if hidden)");

                            string path = "";

                            // Mở hộp thoại OpenFileDialog trên STA Thread (Bắt buộc cho Console)
                            Thread t = new Thread((ThreadStart)(() => {
                                // Tạo Form ảo để ép Dialog nổi lên trên cùng (TopMost)
                                Form dummy = new Form() { TopMost = true, Top = -10000 }; 
                                
                                using (OpenFileDialog ofd = new OpenFileDialog())
                                {
                                    ofd.Title = "Chọn file Project TIA Portal";
                                    // Lọc file TIA Portal theo mọi phiên bản (.ap15, .ap16, .ap17... đến .ap20)
                                    ofd.Filter = "TIA Portal Project (*.ap*)|*.ap*"; 
                                    ofd.RestoreDirectory = true;

                                    if (ofd.ShowDialog(dummy) == DialogResult.OK)
                                    {
                                        path = ofd.FileName;
                                    }
                                }
                            }));

                            t.SetApartmentState(ApartmentState.STA);
                            t.Start();
                            t.Join(); // Đợi người dùng chọn file xong

                            // Kiểm tra nếu người dùng bấm Cancel
                            if (string.IsNullOrEmpty(path))
                            {
                                ConsoleUI.PrintError("Operation cancelled. No project selected.");
                                Console.ReadKey();
                            }
                            else
                            {
                                Console.WriteLine($"\nSelected: {Path.GetFileName(path)}");
                                ConsoleUI.PrintStep("Opening Project. Please wait...");

                                // Gọi hàm mở Project của bạn
                                if (_tiaEngine.CreateTIAproject(path, "", false))
                                {
                                    _currentProjectName = Path.GetFileNameWithoutExtension(path);
                                    ConsoleUI.PrintSuccess("Project Opened Successfully!");
                                    currentState = AppState.TIA_Processing;
                                }
                                else 
                                {
                                    ConsoleUI.PrintError("Failed to open project.");
                                }
                                Console.ReadKey();
                            }
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
                            "13. Update Firmware",
                            "14. Generate SCADA from JSON",
                            "15. Setup HMI-PLC Connection (Unified)",
                            "16. Create HMI Tag (WinCC Unified)",
                            "17. Import Graphics to Project (WinCC Unified)",
                            "18. Import PLC Tags from CSV",
                            "19. Export Symbol Paths from Screen (New)"
                            
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
                    else if (procChoice.Contains("14."))
                    {
                        HandleJsonDrawing();
                     
                        
                    }
                    else if (procChoice.Contains("15."))
                    {
                           SetupHmiConnection();
                           
                    }    
                    else if (procChoice.Contains("16."))
                    {
                           ImportTagsMenu();
                           
                    }     
                    else if (procChoice.Contains("17."))
                    {
                        HandleImportGraphics();
                        // HandleExportSample();
                           
                    }
                     else if (procChoice.Contains("18."))
                        ImportPlcTagsMenu();   
                    else if (procChoice.Contains("19.")) // Giả sử 18 là Export Paths
{
    Console.Clear();
    Console.Write("Nhập tên màn hình cần quét: ");
    string screen = Console.ReadLine();
    
    Console.Write("Nhập tên vật thể cụ thể (hoặc để trống nếu quét tất cả): ");
    string target = Console.ReadLine() ?? ""; 

    // Cập nhật dòng bị lỗi (Dòng 470): Thêm tham số thứ 3
    _tiaEngine.ExportAllPathsFromScreen(_currentDeviceName, screen, target);
    
    Console.WriteLine("\nDone. Press any key...");
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
        private static async void HandleJsonDrawing()
{
    Console.Clear();
    ConsoleUI.PrintHeader("WINCC UNIFIED JSON GENERATOR");

    // 1. Kiểm tra kết nối
    if (!_tiaEngine.IsConnected)
    {
        ConsoleUI.PrintError("Please connect to TIA instance first!");
        Console.ReadKey();
        return;
    }

    // 2. Chọn thiết bị HMI (PC-System_1)
    var devices = _tiaEngine.GetPlcList();
    string selectedDevice = ConsoleUI.SelectOption("Choose HMI Device:", devices.ToArray());
    if (string.IsNullOrEmpty(selectedDevice)) return;

    // 3. Chọn file JSON cấu hình SCADA
    string filePath = SelectJsonFilePath(); 
    if (string.IsNullOrEmpty(filePath)) return;

    try
    {
        // BƯỚC 1: Đọc và giải mã JSON
        string jsonContent = File.ReadAllText(filePath);
        var screenData = JsonConvert.DeserializeObject<ScadaScreenModel>(jsonContent);

        if (screenData == null || string.IsNullOrEmpty(screenData.ScreenName))
        {
            ConsoleUI.PrintError("JSON invalid or missing ScreenName.");
            return;
        }

        // BƯỚC 2: TẠO MÀN HÌNH MỚI (FIX LỖI NULL REFERENCE)
        ConsoleUI.PrintStep($"Checking screen: {screenData.ScreenName}...");
        _tiaEngine.CreateUnifiedScreen(selectedDevice, screenData.ScreenName); // Hàm này đảm bảo màn hình luôn tồn tại

        // BƯỚC 3: Mapping tên ảnh thực tế (Phần code cũ của Otis giữ nguyên)
        List<string> tiaGraphics = _tiaEngine.GetProjectGraphicsNames();
        // ... (Logic Mapping Layer/Items của bạn giữ nguyên tại đây) ...
        ValidateAndFixGraphics(screenData);

        // BƯỚC 4: Ra lệnh vẽ (Bây giờ chắc chắn màn hình đã tồn tại)
        ConsoleUI.PrintStep($"DRAWING TO WINCC UNIFIED...");
        await Task.Run(() => { 
            _tiaEngine.GenerateScadaScreenFromData(selectedDevice, screenData); 
        });

        ConsoleUI.PrintSuccess("Vẽ thành công!");
    }
    catch (Exception ex) { ConsoleUI.PrintError($"Lỗi: {ex.Message}"); }
    
    Console.WriteLine("\nPress any key to return...");
    Console.ReadKey();
}

// Hàm phụ trợ chọn file để code trông gọn hơn
private static string SelectJsonFilePath()
{
    string path = "";
    Thread t = new Thread(() => {
        Form dummy = new Form() { TopMost = true, Top = -10000 }; 
        using (OpenFileDialog ofd = new OpenFileDialog()) {
            ofd.Title = "Chọn file cấu hình SCADA JSON";
            ofd.Filter = "JSON Files (*.json)|*.json";
            if (ofd.ShowDialog(dummy) == DialogResult.OK) path = ofd.FileName;
        }
    });
    t.SetApartmentState(ApartmentState.STA);
    t.Start(); t.Join();
    return path;
}

private static void ValidateAndFixGraphics(ScadaScreenModel screenData)
{
    ConsoleUI.PrintStep("Đang kiểm tra kho ảnh trong TIA Portal...");

    try
    {
        // 1. Lấy danh sách ảnh hiện có trong TIA (Đảm bảo danh sách không null)
        List<string> tiaGraphics = _tiaEngine.GetProjectGraphicsNames() ?? new List<string>();

        // 2. Thu thập TẤT CẢ các Items từ TẤT CẢ các Layers (Sửa lỗi source null)
        // Dùng SelectMany để làm phẳng cấu trúc Layers -> Items
        var allItems = screenData.Layers?
            .Where(l => l.Items != null)
            .SelectMany(l => l.Items)
            .ToList() ?? new List<ScadaItemModel>();

        if (allItems.Count == 0)
        {
            Console.WriteLine("      [!] Cảnh báo: Không tìm thấy đối tượng nào trong file JSON.");
            return;
        }

        // 3. Lấy danh sách các Type cần dùng (không trùng lặp, bỏ qua hình vẽ cơ bản)
        var requiredTypes = allItems
            .Select(i => i.Type)
            .Distinct()
            .Where(t => !string.IsNullOrEmpty(t) && 
                        t != "Button" && 
                        t != "Rectangle" && 
                        t != "Gauge") 
            .ToList();

        foreach (var type in requiredTypes)
        {
            // Kiểm tra xem đã có ảnh nào trong TIA chứa từ khóa 'type' chưa
            bool exists = tiaGraphics.Any(g => g.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0);

            if (!exists)
            {
                ConsoleUI.PrintError($"Thiếu ảnh cho loại thiết bị: '{type}'");
                Console.WriteLine($"      -> Vui lòng chọn file ảnh để nạp vào TIA cho '{type}'...");

                // 4. Mở hộp thoại chọn file (STA Thread)
                string selectedFile = "";
                Thread t = new Thread(() => {
                    Form dummy = new Form() { TopMost = true, Top = -10000 };
                    using (OpenFileDialog ofd = new OpenFileDialog()) {
                        ofd.Title = $"Nạp ảnh cho {type}";
                        ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp";
                        if (ofd.ShowDialog(dummy) == DialogResult.OK) selectedFile = ofd.FileName;
                    }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start(); t.Join();

                if (!string.IsNullOrEmpty(selectedFile))
                {
                    // 5. Nạp trực tiếp vào Project Graphics
                    // Dùng chính tên 'type' làm tên định danh trong TIA để khớp 100%
                    bool success = _tiaEngine.ImportGraphic(type, selectedFile);
                    if (success)
                    {
                        ConsoleUI.PrintSuccess($"Đã nạp ảnh '{type}' vào TIA thành công.");
                        tiaGraphics.Add(type); // Cập nhật danh sách tạm để không hỏi lại
                    }
                }
                else
                {
                    ConsoleUI.PrintResult($"Bạn chưa chọn ảnh cho '{type}'. Đối tượng này có thể bị trắng khi vẽ.");
                }
            }
        }
    }
    catch (Exception ex)
    {
        ConsoleUI.PrintError($"Lỗi trong quá trình kiểm tra kho ảnh: {ex.Message}");
    }
}
       private static void HandleImportGraphics()
{
    Console.Clear();
    ConsoleUI.PrintHeader("IMPORT GRAPHIC TO WINCC UNIFIED");

    // 1. Kiểm tra kết nối
    if (!_tiaEngine.IsConnected)
    {
        ConsoleUI.PrintError("Please connect to TIA instance first!");
        Console.WriteLine("Press any key to return...");
        Console.ReadKey();
        return;
    }

    // 2. Menu chọn chế độ
    Console.WriteLine("Select Import Mode:");
    Console.WriteLine("1. Import Single Image (Chọn 1 file)");
    Console.WriteLine("2. Import Batch (Nạp toàn bộ ảnh trong 1 thư mục)");
    Console.Write("\nYour choice: ");
    string choice = Console.ReadLine();

    if (choice == "1")
    {
        ImportSingleGraphic();
    }
    else if (choice == "2")
    {
        ImportBatchGraphics();
    }
    else
    {
        ConsoleUI.PrintError("Invalid choice.");
    }

    Console.WriteLine("\nPress any key to return...");
    Console.ReadKey();
}

private static void ImportSingleGraphic()
{
    Console.WriteLine("\n--- Mode: Single Import ---");
    string imagePath = "";
    
    Thread t = new Thread((ThreadStart)(() => {
        Form dummy = new Form() { TopMost = true, Top = -10000 }; 
        using (OpenFileDialog ofd = new OpenFileDialog()) {
            ofd.Title = "Chọn file ảnh đơn lẻ";
            ofd.Filter = "Image Files (*.svg;*.png;*.jpg)|*.svg;*.png;*.jpg";
            if (ofd.ShowDialog(dummy) == DialogResult.OK) imagePath = ofd.FileName;
        }
    }));
    t.SetApartmentState(ApartmentState.STA);
    t.Start(); t.Join();

    if (string.IsNullOrEmpty(imagePath)) return;

    Console.Write("Enter name in TIA (Leave blank for file name): ");
    string graphicName = Console.ReadLine();
    if (string.IsNullOrEmpty(graphicName)) graphicName = Path.GetFileNameWithoutExtension(imagePath);

    bool isSuccess = _tiaEngine.AddPngToProjectGraphics(imagePath, graphicName);
    if (isSuccess) ConsoleUI.PrintSuccess($"Imported: {graphicName}");
}
private static void ImportBatchGraphics()
{
    Console.WriteLine("\n--- Mode: Batch Import ---");
    string folderPath = "";

    Thread t = new Thread((ThreadStart)(() => {
        Form dummy = new Form() { TopMost = true, Top = -10000 };
        using (FolderBrowserDialog fbd = new FolderBrowserDialog()) {
            fbd.Description = "Chọn thư mục chứa kho ảnh PNG của bạn";
            if (fbd.ShowDialog(dummy) == DialogResult.OK) folderPath = fbd.SelectedPath;
        }
    }));
    t.SetApartmentState(ApartmentState.STA);
    t.Start(); t.Join();

    if (!string.IsNullOrEmpty(folderPath))
    {
        Console.WriteLine($"Scanning folder: {folderPath}...");
        // Gọi hàm nạp hàng loạt đã viết trong Engine TIA_V20
        _tiaEngine.ImportAllImagesFromFolder(folderPath);
        ConsoleUI.PrintSuccess("Batch import process finished.");
    }
}

private static void SyncJsonWithTiaGraphics(string jsonPath)
{
    Console.WriteLine("\n--- [SYNC] ĐỒNG BỘ JSON VỚI KHO ẢNH TIA PORTAL ---");

    try
    {
        // 1. Lấy danh sách ảnh thực tế đang có trong Project Graphics của TIA
        List<string> tiaGraphics = _tiaEngine.GetProjectGraphicsNames();

        if (tiaGraphics == null || tiaGraphics.Count == 0)
        {
            Console.WriteLine("      [!] Cảnh báo: Kho ảnh trong TIA đang trống.");
            return;
        }

        // 2. Đọc và giải mã file JSON
        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"      [!] Lỗi: Không tìm thấy file JSON tại {jsonPath}");
            return;
        }

        string jsonContent = File.ReadAllText(jsonPath);
        // Lưu ý: Dùng ScadaScreenModel trực tiếp (bỏ TIA_V20. phía trước)
        var scadaData = JsonConvert.DeserializeObject<ScadaScreenModel>(jsonContent);

        if (scadaData == null || scadaData.Items == null) return;

        bool isUpdated = false;
        int matchCount = 0;

        // 3. Thực hiện so khớp tên ảnh
        foreach (var item in scadaData.Items)
        {
            // Tìm tấm ảnh trong TIA mà tên của nó xuất hiện trong Name hoặc Type của Item
            // Sử dụng IndexOf để tương thích với mọi phiên bản .NET
            string bestMatch = tiaGraphics.FirstOrDefault(g => 
                (item.Name != null && item.Name.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0) || 
                (item.Type != null && item.Type.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0));

            if (!string.IsNullOrEmpty(bestMatch))
            {
                // Khởi tạo Properties nếu nó bị null trong JSON
                if (item.Properties == null) item.Properties = new Dictionary<string, object>();

                // Gán hoặc cập nhật tên ảnh thực tế vào thuộc tính GraphicName
                item.Properties["GraphicName"] = bestMatch;
                isUpdated = true;
                matchCount++;
                Console.WriteLine($"      [MATCH] '{item.Name}' -> Khớp với ảnh: '{bestMatch}'");
            }
            else
            {
                Console.WriteLine($"      [?] '{item.Name}': Không tìm thấy ảnh phù hợp trong TIA.");
            }
        }

        // 4. Ghi đè lại file JSON nếu có sự thay đổi
        if (isUpdated)
        {
            string output = JsonConvert.SerializeObject(scadaData, Formatting.Indented);
            File.WriteAllText(jsonPath, output);
            Console.WriteLine($"\n      [OK] Đã cập nhật {matchCount} đối tượng vào file JSON.");
        }
        else
        {
            Console.WriteLine("\n      [Info] Không có thay đổi nào cần cập nhật.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"      [ERROR] Lỗi thực thi Sync: {ex.Message}");
    }
}
// private static void HandleExportSample()
// {
//     Console.Clear();
//     Console.WriteLine("--- Step 0: Exporting Sample Format from TIA ---");
    
//     // Định nghĩa nơi lưu file mẫu
//     string exportPath = @"C:\Capstone Project\TIA_Graphic_Format.xml";

//     // Gọi engine để thực hiện
//     // Giả sử đối tượng TIA_V20 của bạn tên là _tiaEngine
//     _tiaEngine.ExportGraphicSample(exportPath);

//     Console.WriteLine("\n[HƯỚNG DẪN CHO OTIS]:");
//     Console.WriteLine("1. Otis hãy mở file 'TIA_Graphic_Format.xml' bằng Notepad++ hoặc VS Code.");
//     Console.WriteLine("2. Tìm thẻ <Document xmlns=\"...\"> để xem Namespace chuẩn.");
//     Console.WriteLine("3. Tìm thẻ bao quanh ID=\"0\" để xem tên Class chuẩn của WinCC Unified.");
//     Console.WriteLine("\nPress any key to return...");
//     Console.ReadKey();
// }


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
        // Giả sử đây là một phần trong Navigator.cs của bạn
        private static void SetupHmiConnection() 
        {
            Console.WriteLine("\n--- THIẾT LẬP KẾT NỐI (TẠO & SỬA LỒNG GHÉP) ---");
            
            Console.Write("Nhập tên HMI(PC-System_1):  ");
            string hmi = Console.ReadLine();
            
            Console.Write("Nhập IP HMI(192.168.0.2): ");
            string hmiIp = Console.ReadLine();
            
            Console.Write("Nhập IP PLC(192.168.1.251): ");
            string plcIp = Console.ReadLine();

            // Chạy hàm lồng ghép để tránh lỗi format khi tạo mới
            string result = _tiaEngine.CreateUnifiedConnectionCombined(hmi, hmiIp, plcIp, "Connection_1");
            ConsoleUI.PrintResult(result);                      
            Console.WriteLine("\nPress any key to return...");
            Console.ReadKey();
        }

       private static void ImportTagsMenu()
{
    Console.WriteLine("\n--- NẠP HMI TAGS TỪ FILE CSV (CHỌN FILE) ---");
    Console.Write("Nhập tên HMI (PC-System_1): ");
    string hmi = Console.ReadLine();

    // Khởi tạo và ép luồng chạy Dialog
    Thread t = new Thread(() => {
        using (OpenFileDialog openFileDialog = new OpenFileDialog())
        {
            openFileDialog.Title = "Chọn file danh sách HMI Tags";
            openFileDialog.Filter = "CSV files (*.csv)|*.csv";
            
            // Cửa sổ sẽ hiện lên trên cùng (TopMost)
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filePath = openFileDialog.FileName;
                // Trả kết quả về luồng chính để Engine xử lý
                _tiaEngine.ImportHmiTagsFromCsv(hmi, filePath);
            }
        }
    });

    t.SetApartmentState(ApartmentState.STA); // Thiết lập chế độ STA cho luồng mới
    t.Start();
    t.Join(); // Đợi luồng chọn file kết thúc mới chạy tiếp Menu
    Console.WriteLine("\nPress any key to return...");
    Console.ReadKey();
}

private static void ImportPlcTagsMenu()
{
    Console.WriteLine("\n--- NẠP PLC TAGS TỪ FILE CSV (CHỌN FILE) ---");
    // Đổi thông báo để người dùng nhập tên PLC (VD: PLC_1)
    Console.Write("Nhập tên PLC (ví dụ: PLC_1): ");
    string plcName = Console.ReadLine();

    // Khởi tạo luồng chạy Dialog (STA là bắt buộc đối với WinForms Dialog)
    Thread t = new Thread(() => {
        using (OpenFileDialog openFileDialog = new OpenFileDialog())
        {
            openFileDialog.Title = "Chọn file danh sách PLC Tags";
            openFileDialog.Filter = "CSV files (*.csv)|*.csv";
            
            // Cửa sổ sẽ hiện lên để chọn file
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filePath = openFileDialog.FileName;
                
                // Gọi hàm nạp Tag cho PLC thay vì HMI
                _tiaEngine.ImportPlcTagsFromCsv(plcName, filePath);
            }
        }
    });

    t.SetApartmentState(ApartmentState.STA); 
    t.Start();
    t.Join(); // Chờ xử lý xong mới quay lại menu chính

    Console.WriteLine("\nNhấn phím bất kỳ để quay lại...");
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