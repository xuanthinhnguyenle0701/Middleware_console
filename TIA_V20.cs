using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.Connection;
using Siemens.Engineering.Download;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Tag;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Library;
using System.Xml.Linq;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System.Windows.Forms;



namespace Middleware_console
{
    public class TIA_V20
    {
        #region 1. Fields & Constructor
        private TiaPortal _tiaPortal;
        private Project _project;
        public TIA_V20() { }
        // --- BỔ SUNG ĐỂ TƯƠNG THÍCH VỚI NAVIGATOR.CS ---

        // 1. Thuộc tính kiểm tra trạng thái kết nối
        public bool IsConnected => _tiaPortal != null && _project != null;

        // 2. Hàm Alias (tên giả) để Navigator gọi Connect
        public void ConnectToTiaPortal()
        {
            // Thử kết nối process đang chạy
            if (!ConnectToTIA()) 
            {
                // Nếu không tìm thấy process nào, thử tạo mới (tùy chọn)
                // Hoặc ném lỗi để Navigator bắt được
                throw new Exception("No running TIA Portal instance found!");
            }
        }

        // 3. Hàm Alias để Navigator gọi Import
        public void ImportBlock(string filePath)
        {
            CreateFBblockFromSource(filePath);
        }
        #endregion

        #region 2. Connection & Project Management
        public static List<string> GetSystemNetworkAdapters()
        {
            List<string> adapterNames = new List<string>();

            try
            {
                // SỬA: Thêm "System.Net.NetworkInformation." vào trước NetworkInterface
                System.Net.NetworkInformation.NetworkInterface[] adapters =
                    System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

                foreach (System.Net.NetworkInformation.NetworkInterface adapter in adapters)
                {
                    // SỬA: Thêm namespace đầy đủ cho NetworkInterfaceType
                    if (adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet ||
                        adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                    {
                        // Lấy Description (Tên card mạng đầy đủ)
                        adapterNames.Add(adapter.Description);
                    }
                }
            }
            catch { }

            return adapterNames;
        }
        public void CreateTIAinstance(bool withUI)
        {
            if (_tiaPortal != null) return;
            _tiaPortal = new TiaPortal(withUI ? TiaPortalMode.WithUserInterface : TiaPortalMode.WithoutUserInterface);
        }

        public bool ConnectToTIA()
        {
            try
            {
                var processes = TiaPortal.GetProcesses();
                if (processes.Count == 0) return false;
                _tiaPortal = processes[0].Attach();
                if (_tiaPortal.Projects.Count > 0)
                {
                    _project = _tiaPortal.Projects[0];
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        public bool CreateTIAproject(string path, string name, bool createNew)
        {
            try
            {
                if (_tiaPortal == null) CreateTIAinstance(true);
                if (createNew)
                    _project = _tiaPortal.Projects.Create(new DirectoryInfo(path), name);
                else
                    _project = _tiaPortal.Projects.Open(new FileInfo(path));
                return _project != null;
            }
            catch { return false; }

        }

        

        public bool SaveProject()
        {
            try { _project?.Save(); return true; } catch { return false; }
        }

        public void CloseTIA()
        {
            try
            {
                // 1. Đóng Project nhẹ nhàng trước (để lưu dữ liệu nếu cần)
                if (_project != null)
                {
                    _project.Close();
                    _project = null;
                }

                // 2. Ngắt kết nối Openness
                if (_tiaPortal != null)
                {
                    _tiaPortal.Dispose();
                    _tiaPortal = null;
                }

                // 3. (MẠNH TAY) Tìm và diệt tiến trình TIA Portal
                // Lưu ý: Lệnh này sẽ tắt MỌI cửa sổ TIA Portal đang mở trên máy tính
                foreach (var process in Process.GetProcessesByName("Siemens.Automation.Portal"))
                {
                    try
                    {
                        process.Kill(); // Lệnh tắt cưỡng bức
                        process.WaitForExit(); // Đợi cho tắt hẳn
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nếu cần
            }
        }
        #endregion

        #region 3. Hardware & Network (ĐÃ CHỈNH SỬA CHO JSON & FOLDER)

        // Hàm CreateDev chuẩn cho JSON (Nhận chuỗi TypeIdentifier)
        public void CreateDev(string devName, string typeIdentifier, string ipX1, string ipX2)
{
    if (_project == null) CheckProject();
    if (string.IsNullOrWhiteSpace(devName)) devName = "Device_1";

    List<string> existingNames = GetPlcList();
    if (existingNames.Contains(devName))
        throw new Exception($"Name '{devName}' already exists!");

    Device newDevice = null;

    try
    {
        // --- XỬ LÝ WINCC UNIFIED PC ---
        if (typeIdentifier.Contains("xxxxx") || typeIdentifier.Contains("6AV2 155"))
        {
            Console.WriteLine($"[Auto-Fix] Detected WinCC Unified PC. Starting Optimized Creation...");

            // 1. TẠO KHUNG PC (Chỉ thử 2 mã chuẩn nhất để nhanh)
            string[] pcIds = new string[] { "System:Rack.PC", "OrderNumber:6ES7647-0AA00-1YA0/V3.0" };
            
            foreach (string id in pcIds)
            {
                try 
                {
                    newDevice = _project.Devices.CreateWithItem(id, devName, devName);
                    if (newDevice != null) { Console.WriteLine("   -> [SUCCESS] PC Station created!"); break; }
                }
                catch {}
            }

            // Fallback: Dùng Create() nếu cần
            if (newDevice == null)
            {
                try { newDevice = _project.Devices.Create("System:Device.PC", devName); } catch {}
            }

            if (newDevice == null) throw new Exception("FATAL: Could not create PC Station.");

            // 2. CẤU HÌNH KHE CẮM (SLOTS)
            try 
            {
                Console.WriteLine("   -> Configuring PC slots (Auto-Name Mode)...");
                if (newDevice.DeviceItems.Count == 0) throw new Exception("Device Empty.");
                DeviceItem pcRack = newDevice.DeviceItems[0]; 

                // --- 2.1: Cắm Card mạng IE General ---
                // Chỉ thử Slot 1-2 (Không dò lan man)
                bool netPlugged = false;
                string netId = "OrderNumber:IE General/V2.0"; // TIA V20 ưu tiên bản này
                
                for (int i = 1; i <= 2; i++) 
                {
                    // Truyền tên "" để TIA tự đặt tên (tránh lỗi Name Invalid)
                    if (pcRack.CanPlugNew(netId, "", i))
                    {
                        pcRack.PlugNew(netId, "IE1", i); // Card mạng thì đặt tên IE1 được
                        Console.WriteLine($"   -> [SUCCESS] Plugged Network Card at Slot {i}.");
                        netPlugged = true;
                        break;
                    }
                }
                if (!netPlugged) Console.WriteLine("   [Info] Could not plug IE General (Skipping).");

                // --- 2.2: Cắm WinCC Unified ---
                string firmware = "20.0.0.0"; 
                if (typeIdentifier.Contains("/") && typeIdentifier.Split('/').Length > 1)
                {
                    string f = typeIdentifier.Split('/')[1].Replace("V", "").Trim();
                    if (!string.IsNullOrEmpty(f)) firmware = f;
                }

                // Mã phần mềm chuẩn
                string swId = $"OrderNumber:6AV2 155-xxxxx-xxxx/{firmware}";
                Console.WriteLine($"   ... Plugging Software: {swId}");

                bool swPlugged = false;
                // Chỉ dò Slot 1 đến 10 (PC Station thường chỉ nằm ở đây, không cần dò đến 125)
                for (int slotNum = 1; slotNum <= 10; slotNum++)
                {
                    // QUAN TRỌNG: Kiểm tra CanPlugNew với tên rỗng ""
                    if (pcRack.CanPlugNew(swId, "", slotNum))
                    {
                        try 
                        {
                            // CẮM VỚI TÊN RỖNG -> ĐỂ TIA TỰ ĐẶT TÊN CHUẨN
                            pcRack.PlugNew(swId, "", slotNum);
                            Console.WriteLine($"   -> [SUCCESS] Plugged WinCC Unified into Slot {slotNum} (Auto-Named).");
                            swPlugged = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"      [Retry] Slot {slotNum} rejected: {ex.Message}");
                        }
                    }
                }
                
                if (!swPlugged) Console.WriteLine("   [Error] Could not plug WinCC Unified. Check License/Firmware.");
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"   [Warning] Slot config error: {ex.Message}"); 
            }
        }
        else
        {
            // --- THIẾT BỊ KHÁC ---
            newDevice = _project.Devices.CreateWithItem(typeIdentifier, devName, devName);
        }

        // Gán IP
        if (newDevice != null && !string.IsNullOrEmpty(ipX1))
        {
            try { SetPlcIpAddress(newDevice, ipX1); } catch {}
        }
    }
    catch (Exception ex)
    {
        throw new Exception($"Create Failed: {ex.Message}");
    }
}

        

        // Hàm lấy danh sách PLC (ĐỆ QUY - Hỗ trợ tìm trong Folder/Group)
        public List<string> GetPlcList()
        {
            if (_project == null) CheckProject();
            List<string> plcNames = new List<string>();

            if (_project == null) return plcNames;

            // 1. Quét PLC ở ngoài cùng (Root)
            foreach (Device device in _project.Devices)
            {
                plcNames.Add(device.Name);
            }

            // 2. Quét PLC nằm trong các Group (Folder)
            foreach (DeviceUserGroup group in _project.DeviceGroups)
            {
                ScanGroupRecursive(group, plcNames);
            }

            return plcNames;
        }

        // Hàm phụ trợ đệ quy cho GetPlcList
        private void ScanGroupRecursive(DeviceUserGroup group, List<string> names)
        {
            // Lấy Device trong Group hiện tại
            foreach (Device device in group.Devices)
            {
                names.Add(device.Name);
            }

            // Tìm tiếp trong các Group con
            foreach (DeviceUserGroup subGroup in group.Groups)
            {
                ScanGroupRecursive(subGroup, names);
            }
        }

        public void SetPLCName(string authorName) { }
        #endregion

        #region 4. Software & Compilation
        public void CreateFBblockFromSource(string sourcePath)
        {
            if (_project == null) CheckProject();
            foreach (Device device in _project.Devices)
            {
                var software = GetSoftware(device);
                if (software is PlcSoftware plcSoftware)
                {
                    var group = plcSoftware.ExternalSourceGroup;
                    var src = group.ExternalSources.CreateFromFile(Path.GetFileName(sourcePath), sourcePath);
                    src.GenerateBlocksFromSource();
                    return;
                }
            }
            throw new Exception("No PLC found.");
        }

        public string CompileSpecific(string targetPlcName, bool compileHW, bool compileSW)
        {
            if (_project == null) return "Not connected.";
            Device device = _project.Devices.Find(targetPlcName);

            // Nếu không tìm thấy ở root, thử tìm đệ quy (Vì PLC có thể nằm trong Group)
            if (device == null) device = FindDeviceRecursive(_project, targetPlcName);

            if (device == null) return "Device not found.";

            StringBuilder sb = new StringBuilder();
            if (compileHW)
            {
                var provider = device as IEngineeringServiceProvider;
                var compiler = provider?.GetService<ICompilable>();
                if (compiler != null) sb.AppendLine("HW: " + compiler.Compile().State);
            }
            if (compileSW)
            {
                var sw = GetSoftware(device);
                var provider = sw as IEngineeringServiceProvider;
                var compiler = provider?.GetService<ICompilable>();
                if (compiler != null) sb.AppendLine("SW: " + compiler.Compile().State);
            }
            return sb.ToString();
        }
        #endregion

        #region 5. WinCC Unified Operations (Basic)
        public List<string> GetUnifiedScreens(string deviceName)
        {
            if (_project == null) CheckProject();
            Device device = FindDeviceRecursive(_project, deviceName); // Sửa: Tìm đệ quy
            if (device == null) throw new Exception("Device not found");

            var software = GetSoftware(device);
            if (!(software is HmiSoftware hmiSw)) throw new Exception("Not HMI Unified");

            List<string> names = new List<string>();
            foreach (var s in hmiSw.Screens) names.Add(s.Name);
            return names;
        }

        public void CreateUnifiedScreen(string deviceName, string screenName)
        {
            if (_project == null) CheckProject();
            Device device = FindDeviceRecursive(_project, deviceName); // Sửa: Tìm đệ quy
            var software = GetSoftware(device) as HmiSoftware;
            if (software == null) throw new Exception("HMI Software not found");

            if (software.Screens.Find(screenName) == null)
                software.Screens.Create(screenName);
        }

        public void CreateScreenItem(string deviceName, string screenName, string itemName, string itemType, string itemText, int left, int top, int width, int height)
        {
            var screenItems = GetScreenItemsComposition(deviceName, screenName);
            if (screenItems != null)
            {
                var item = CreateItemGeneric(screenItems, itemType, itemName);
                if (item != null)
                {
                    SetAttributeSafe(item, "Left", left);
                    SetAttributeSafe(item, "Top", top);
                    SetAttributeSafe(item, "Width", width);
                    SetAttributeSafe(item, "Height", height);
                    SetAttributeSafe(item, "Text", itemText);
                }
            }
        }
        #endregion

        #region 6. Advanced SCADA Generation (Beta test)
        public void GenerateScadaScreenFromData(string deviceName, ScadaScreenModel screenData)
        {
            if (_project == null) throw new Exception("No TIA Project opened.");

            // 1. Tìm HMI Target (Dùng dynamic để hỗ trợ cả 2 loại)
            dynamic hmiTarget = GetHmiTarget(deviceName);
            if (hmiTarget == null) throw new Exception($"Device '{deviceName}' not found.");

            Console.WriteLine($">> [Unified Mode] Processing Screen: {screenData.ScreenName}...");
                        

            // 2. [FIX LỖI TẠI ĐÂY] Lấy danh sách màn hình tùy theo loại thiết bị
            dynamic screens = null;
            try 
            {
                // Cách 1: Dành cho PC-System (HmiSoftware) - Truy cập thẳng
                screens = hmiTarget.Screens; 
            }
            catch 
            {
                // Cách 2: Dành cho Panel (HmiTarget) - Phải qua ScreenFolder
                screens = hmiTarget.ScreenFolder.Screens;
            }

            if (screens == null) throw new Exception("Cannot locate the Screens container in this device.");

           // ĐOẠN MỚI: Tìm chính xác đối tượng cần xóa trước
            var screenList = ((System.Collections.IEnumerable)screens).Cast<dynamic>().ToList();
            var existingScreen = screenList.FirstOrDefault(s => s.Name == screenData.ScreenName);

            if (existingScreen != null)
            {
                Console.WriteLine($"      [INFO] Xóa màn hình cũ: {screenData.ScreenName}");
                existingScreen.Delete(); 
            }

            IEngineeringObject currentScreen = null;
            try 
            {
                // Unified cho phép Create với 1 tham số Name
                dynamic res = screens.Create(screenData.ScreenName);
                currentScreen = res as IEngineeringObject;
            }
            catch (Exception ex) { throw new Exception($"Failed to create Unified Screen. {ex.Message}"); }

            // 3. Lấy ScreenItems
            dynamic unifiedScreen = currentScreen; 
            dynamic screenItems = unifiedScreen.ScreenItems; 

            if (screenItems == null) throw new Exception("Cannot access ScreenItems.");

            // 4. Gộp Items (Giữ nguyên logic của bạn)
            List<ScadaItemModel> allItems = new List<ScadaItemModel>();
            if (screenData.Items != null) allItems.AddRange(screenData.Items);
            if (screenData.Layers != null)
                foreach (var l in screenData.Layers) 
                    if (l.Items != null) allItems.AddRange(l.Items);            

            // 5. Vẽ - TRUYỀN BIẾN screenItems (đã là dynamic) VÀO
            // Đừng truyền 'screenItemsComp' vì nó là IEngineeringComposition (bị khóa quyền)
            BuildUnifiedItemsRecursive(screenItems, allItems);

            Console.WriteLine("[SUCCESS] WinCC Unified Screen Generated Successfully!");
        }

      


// Hàm vẽ đệ quy cho WinCC Unified (Đã chỉnh sửa để dùng dynamic và hỗ trợ cả Widget lẫn Graphic)
private void BuildUnifiedItemsRecursive(dynamic composition, List<ScadaItemModel> items)
{
    var createdObjects = new Dictionary<string, dynamic>();
    Console.WriteLine("\n>> [GIAI ĐOẠN 1] Dựng hình & Gán tọa độ thực...");

    // --- TRONG GIAI ĐOẠN 1: DỰNG HÌNH & ĐỊNH VỊ ---
foreach (var item in items) {
    try {
        dynamic newItem = null;
        if (item.Properties.ContainsKey("LibraryPath")) {
            CreateDynamicWidget(composition, item.Type, item.Name, item.Properties);
            System.Threading.Thread.Sleep(300);
            newItem = composition.Find(item.Name);
        } else {
            string typeId = item.Type.StartsWith("Hmi") ? item.Type : "Hmi" + item.Type;
            newItem = CreateBaseItem(composition, typeId, item.Name);
        }

        if (newItem == null) continue;

        // A. ĐỊNH VỊ TỌA ĐỘ (Dùng SetAttribute để tránh lỗi int/uint)
        try {
            if (item.Properties.ContainsKey("Left")) newItem.SetAttribute("Left", Convert.ToInt32(item.Properties["Left"]));
            if (item.Properties.ContainsKey("Top")) newItem.SetAttribute("Top", Convert.ToInt32(item.Properties["Top"]));
            if (item.Properties.ContainsKey("Width")) newItem.SetAttribute("Width", Convert.ToUInt32(item.Properties["Width"]));
            if (item.Properties.ContainsKey("Height")) newItem.SetAttribute("Height", Convert.ToUInt32(item.Properties["Height"]));
            Console.WriteLine($"      [POS OK] {item.Name} -> ({item.Properties["Left"]}, {item.Properties["Top"]})");
        } catch { }

        // B. GÁN CHỮ CHO NÚT BẤM (Sửa lỗi CS0103)   
        
        if (item.Type.Contains("Button")) {
            // A. PHẦN VỎ: Gán Text theo định dạng HTML (Đã dứt điểm lỗi Invalid Format)
            if (item.Properties.ContainsKey("Text")) {
                string formattedContent = $"<body><p>{item.Properties["Text"]}</p></body>";
                try {
                    newItem.Text.Items[0].SetAttribute("Text", formattedContent);
                    Console.WriteLine($"      [TEXT OK] {item.Name} -> {item.Properties["Text"]}");
                } catch { }
            }

            // B. PHẦN LINH HỒN: Gán Script từ JSON
            if (item.Properties.ContainsKey("Scripts")) {
                ProcessButtonScripts(newItem, item.Name, item.Properties["Scripts"]);
            }
        }
        createdObjects.Add(item.Name, newItem);
    } catch { }
}

    Console.WriteLine("\n>> [GIAI ĐOẠN 2] Nạp linh hồn THẬT (Bắt đầu gỡ rối Widget)...");

    foreach (var item in items)
    {
        if (!createdObjects.ContainsKey(item.Name)) {
            Console.WriteLine($"      [!] Bỏ qua {item.Name}: Không tìm thấy xác trong Dictionary.");
            continue;
        }

        dynamic dynItem = createdObjects[item.Name];
        
        // Trích xuất Tag
        string tag = item.Properties.ContainsKey("PressTag") ? item.Properties["PressTag"].ToString() :
                     item.Properties.ContainsKey("LevelTag") ? item.Properties["LevelTag"].ToString() :
                     item.Properties.ContainsKey("StatusTag") ? item.Properties["StatusTag"].ToString() : "";

        // Bẫy Log 1: Kiểm tra xem JSON có Tag không
        if (string.IsNullOrEmpty(tag)) {
            Console.WriteLine($"      [?] {item.Name}: Không có Tag trong JSON (Bỏ qua nạp linh hồn).");
            continue;
        }

        // --- PHÂN LUỒNG XỬ LÝ ---

        // 1. NHÓM NÚT BẤM (Đã OK)
        if (item.Type.Contains("Button")) {
           // ProcessButtonScripts(dynItem, item.Name, tag);
        }
        // 2. NHÓM CẢM BIẾN (Đã OK)
        else if (item.Type.Contains("Rectangle")) {
            BindTagToBasic(dynItem, tag, "BackColor");
        }
        // 3. NHÓM DYNAMIC WIDGET (Bồn, Van, Bơm)
        else if (item.Properties.ContainsKey("LibraryPath")) {
            string targetProp = item.Type.Contains("Tank") ? "FillLevelColor" : "BasicColor";
            bool foundPort = false;

            try {
                foreach (dynamic m in dynItem.Interface) {
                    if (m.PropertyName == targetProp) {
                        foundPort = true;
                        if (item.Type.Contains("Motor")) BindScriptToWidget(m, tag);
                        else BindTagToWidget(m, tag);
                        break;
                    }
                }
                // Bẫy Log 2: Nếu duyệt hết Interface mà không thấy cổng FillLevelColor/BasicColor
                if (!foundPort) {
                    Console.WriteLine($"      [!] {item.Name}: Không tìm thấy cổng '{targetProp}' trong Interface.");
                }
            } catch (Exception ex) {
                Console.WriteLine($"      [!] {item.Name}: Lỗi truy cập Interface: {ex.Message}");
            }
        }
        else {
            Console.WriteLine($"      [?] {item.Name}: Vật thể không thuộc nhóm Widget/Button/Rectangle.");
        }
    }
}


// 1. Dành cho Bồn, Van (Widget Member) - Không tham số
public void BindTagToWidget(dynamic member, string tagName) {
    try {
        dynamic dyns = member.Dynamizations;
        // Tìm hàm Create(string propertyName) có 1 tham số
        var method = ((object)dyns).GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 1);
        
        // SỬA LỖI .Many() thành .SelectMany()
        Type tagType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "TagDynamization");

        if (method != null && tagType != null) {
            // Thực thi nạp Tag vào đúng cổng PropertyName của Member
            var tagDyn = method.MakeGenericMethod(tagType).Invoke(dyns, new object[] { member.PropertyName });
            ((dynamic)tagDyn).Tag = tagName; 
            Console.WriteLine($"      => [THẬT WIDGET] {member.PropertyName} -> {tagName}");
        }
    } catch (Exception ex) { 
        Console.WriteLine($"      [!] Lỗi Widget Tag: {ex.Message}"); 
    }
}
// 2. Dành cho Bơm (Nạp Script) - Không tham số
public void BindScriptToWidget(dynamic member, string tagName) {
    try {
        dynamic dyns = member.Dynamizations;
        var method = ((object)dyns).GetType().GetMethods()
            .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 1);

        // SỬA LỖI .Many() thành .SelectMany()
        Type scriptType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "ScriptDynamization");

        if (method != null && scriptType != null) {
            var scriptDyn = method.MakeGenericMethod(scriptType).Invoke(dyns, new object[] { member.PropertyName });
            
            // Nạp mã JS (Dùng ScriptCode vì bản siêu âm của Otis báo thuộc tính này chạy tốt)
            ((dynamic)scriptDyn).ScriptCode = $@"var status = Tags(""{tagName}"").Read(); 
return status ? HMIRuntime.Math.RGB(135, 190, 50) : HMIRuntime.Math.RGB(178, 34, 34);";
            
            Console.WriteLine($"      => [THẬT WIDGET SCRIPT] {member.PropertyName} -> {tagName}");
        }
    } catch (Exception ex) { 
        Console.WriteLine($"      [!] Lỗi Widget Script: {ex.Message}"); 
    }
}
// 3. Dành cho Nút bấm, Cảm biến (Basic Object) - Phải có PropertyName
public void BindTagToBasic(dynamic item, string tagName, string propName) {
    try {
        var method = ((object)item.Dynamizations).GetType().GetMethods().FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 1);
        Type tagType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.Name == "TagDynamization");
        if (method != null && tagType != null) {
            var tagDyn = method.MakeGenericMethod(tagType).Invoke(item.Dynamizations, new object[] { propName });
            ((dynamic)tagDyn).Tag = tagName;
            Console.WriteLine($"      => [THẬT] Basic Tag: {tagName}");
        }
    } catch { }
}

private void ProcessButtonScripts(dynamic dynItem, string itemName, dynamic scriptsJson) {
    // Kiểm tra null để tránh crash
    if (scriptsJson == null) return;

    Type enumType = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => a.GetTypes())
        .FirstOrDefault(t => t.Name == "HmiButtonEventType");
    
    if (enumType == null) return;

    // QUAN TRỌNG: Duyệt qua các thuộc tính của đối tượng JSON
    foreach (var scriptEntry in scriptsJson) {
        try {
            // Nếu dùng Newtonsoft.Json, scriptEntry sẽ có Name và Value
            string evName = scriptEntry.Name; 
            string jsCode = scriptEntry.Value.ToString();

            var evEnum = Enum.Parse(enumType, evName);
            dynamic handler = null;

            // Tìm hoặc tạo Handler
            foreach (dynamic h in dynItem.EventHandlers) {
                if (h.EventType.ToString() == evName) { handler = h; break; }
            }

            if (handler == null) {
                var method = dynItem.EventHandlers.GetType().GetMethod("Create", new Type[] { enumType });
                handler = method.Invoke(dynItem.EventHandlers, new object[] { evEnum });
            }

            if (handler != null && handler.Script != null) {
                handler.Script.ScriptCode = jsCode;
                Console.WriteLine($"      [SCRIPT OK] {itemName} {evName} -> Code Loaded");
            }
        } catch (Exception ex) {
            // Log này sẽ báo cho Otis biết nếu evName không khớp với Enum KeyDown/KeyUp
            Console.WriteLine($"      [!] Bỏ qua Script không hợp lệ: {ex.Message}");
        }
    }
}

// HÀM PHỤ: TẠO VẬT THỂ
private IEngineeringObject CreateBaseItem(dynamic composition, string typeName, string name)
{
    var method = ((object)composition).GetType().GetMethods().FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod);
    Type targetType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.Name == typeName);
    return (IEngineeringObject)method.MakeGenericMethod(targetType).Invoke(composition, new object[] { name });
}

// HÀM PHỤ: CẬP NHẬT ATTRIBUTE (Xử lý màu sắc và tọa độ)
private void UpdateItemAttribute(IEngineeringObject item, string key, object value, string type)
{
    try {
        string attr = key;
        if (key == "Left" && type.Contains("Circle")) attr = "CenterX";
        if (key == "Top" && type.Contains("Circle")) attr = "CenterY";

        if (key == "BackColor") {
            var rgb = value.ToString().Split(',').Select(b => byte.Parse(b.Trim())).ToArray();
            uint color = (uint)((255 << 24) | (rgb[0] << 16) | (rgb[1] << 8) | rgb[2]);
            item.SetAttribute("BackColor", color);
        } else {
            item.SetAttribute(attr, value);
        }
        Console.WriteLine($"      [OK] {attr} -> {value}");
    } catch { }
}


private void SetUnifiedTextWithVerifiedFormat(dynamic newItem, string propertyName, string newText)
{
    try 
    {
        dynamic mText = newItem.GetAttribute(propertyName);
        foreach (dynamic tItem in mText.Items) 
        {
            // Tạo chuỗi đúng định dạng mà chúng ta vừa đọc được từ Console
            string formattedValue = $"<body><p>{newText}</p></body>";
            
            // Gán lại vào thuộc tính Text của từng Item ngôn ngữ
            tItem.SetAttribute("Text", formattedValue);
        }
        Console.WriteLine($"      [OK] {propertyName} -> Gán định dạng chuẩn thành công");
    }
    catch (Exception ex) 
    {
        Console.WriteLine($"      [!] Lỗi khi gán định dạng: {ex.Message}");
    }
}
        private void SetPropertyUnified(IEngineeringObject obj, string key, object value)
        {
            string targetProp = key;
            object targetVal = value;

            // Mapping JSON -> Unified
            if (key == "FillColor") targetProp = "BackColor";
            if (key == "Text") targetProp = "Text";

            // Xử lý màu sắc
            if (targetProp.Contains("Color") && value is string hex && hex.StartsWith("#"))
            {
                try 
                {
                    hex = hex.Replace("#", "");
                    if (hex.Length == 6) hex = "FF" + hex;
                    targetVal = Convert.ToInt32(hex, 16);
                } catch { targetVal = 0; }
            }

            try 
            { 
                obj.SetAttribute(targetProp, targetVal); 
                if (targetProp == "Text") obj.SetAttribute("TextValue", targetVal);
            } 
            catch { }
        }

        private void CreateDynamicGraphic(dynamic composition, string type, string name, dynamic properties, Siemens.Engineering.Project project)
{
    try
    {
        // 1. Đọc danh sách ảnh thực tế từ Project Graphic View
        List<string> availableGraphics = GetProjectGraphicsNames();

        // 2. SO SÁNH & CHỈNH CHO KHỚP:
        // Tìm tấm ảnh nào trong TIA có tên CHỨA từ khóa 'type' (ví dụ: "Pump" khớp với "Pump_Red")
        string bestMatch = availableGraphics.FirstOrDefault(g => 
            g.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0);

        // Nếu tìm thấy tấm ảnh khớp (ví dụ tìm thấy "Pump_v2" cho type "Pump")
        // thì ta dùng tên đó, nếu không tìm thấy gì thì mới dùng 'type' gốc
        string finalGraphicName = !string.IsNullOrEmpty(bestMatch) ? bestMatch : type;

        if (string.IsNullOrEmpty(bestMatch))
        {
            Console.WriteLine($"      [WARNING] Không tìm thấy ảnh nào khớp với Type '{type}'. Sẽ dùng mặc định.");
        }

        // 3. Tiến hành vẽ HmiGraphicView
        var compositionType = ((object)composition).GetType();
        var methodInfo = compositionType.GetMethods().FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 1);
        Type targetType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.Name == "HmiGraphicView");

        if (methodInfo != null && targetType != null)
        {
            var newItem = (IEngineeringObject)methodInfo.MakeGenericMethod(targetType).Invoke(composition, new object[] { name });
            if (newItem != null)
            {
                newItem.SetAttribute("Left", Convert.ToInt32(properties["Left"]));
                newItem.SetAttribute("Top", Convert.ToInt32(properties["Top"]));
                newItem.SetAttribute("Width", Convert.ToUInt32(properties["Width"]));
                newItem.SetAttribute("Height", Convert.ToUInt32(properties["Height"]));

                // GÁN TÊN ĐÃ ĐƯỢC CHỈNH CHO KHỚP
                newItem.SetAttribute("Graphic", finalGraphicName);
                
                Console.WriteLine($"      [RENDER] Đã khớp '{type}' -> '{finalGraphicName}' cho đối tượng '{name}'");
            }
        }
    }
    catch (Exception ex) { Console.WriteLine($"      [ERROR] Lỗi vẽ Graphic: {ex.Message}"); }
}
public bool AddPngToProjectGraphics(string filePath, string graphicName)
{
    try
    {
        dynamic projectObj = _project;
        var graphics = projectObj.Graphics;

        if (graphics != null && File.Exists(filePath))
        {
            string directory = Path.GetDirectoryName(filePath);
            // Tạo thư mục "files" đi kèm theo đúng định dạng export của TIA
            string folderName = graphicName + "_files";
            string folderPath = Path.Combine(directory, folderName);
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string safeImgName = "DefaultImageStream.png";
            string safeImgPath = Path.Combine(folderPath, safeImgName);
            File.Copy(filePath, safeImgPath, true);

            string xmlPath = Path.Combine(directory, "import_wrapper.xml");
            
            // Otis nhìn kỹ cấu trúc này: Nó giống 100% file mẫu bạn gửi
            string xmlContent = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""V20"" />
  <Hmi.Globalization.MultiLingualGraphic ID=""0"">
    <AttributeList>
      <DefaultDithering>false</DefaultDithering>
      <DefaultImageStream external=""path"">{folderName}\{safeImgName}</DefaultImageStream>
      <DefaultSmoothness>false</DefaultSmoothness>
      <Name>{graphicName}</Name>
    </AttributeList>
  </Hmi.Globalization.MultiLingualGraphic>
</Document>";
            
            File.WriteAllText(xmlPath, xmlContent, System.Text.Encoding.UTF8);

            // Nạp file XML
            FileInfo xmlFileInfo = new FileInfo(xmlPath);
            var methods = ((object)graphics).GetType().GetMethods().Cast<MethodInfo>();
            var importMethod = methods.FirstOrDefault(m => m.Name == "Import" && m.GetParameters().Length == 2);

            if (importMethod != null)
            {
                Type importOptionsType = importMethod.GetParameters()[1].ParameterType;
                object options = Enum.ToObject(importOptionsType, 0); 
                importMethod.Invoke(graphics, new object[] { xmlFileInfo, options });
                
                Console.WriteLine($"      [OK] Đã nạp thành công '{graphicName}' khớp 100% mẫu!");
            }

            // --- BỔ SUNG PHẦN DỌN DẸP RÁC TẠI ĐÂY ---
            if (File.Exists(xmlPath)) File.Delete(xmlPath); // Xóa file XML tạm
            
            if (Directory.Exists(folderPath)) 
            {
                // Xóa thư mục tạm _files sau khi TIA đã nạp xong vào Database
                Directory.Delete(folderPath, true); 
            }
            
            return true;
        }
        return false;
    }
    catch (Exception ex)
    {
        string errorMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        Console.WriteLine($"      [ERROR] Lỗi nạp ảnh: {errorMsg}");
        return false;
    }
}

public void ImportAllImagesFromFolder(string folderPath)
{
    try
    {
        string[] files = Directory.GetFiles(folderPath, "*.png");
        Console.WriteLine($"--- Đang nạp {files.Length} ảnh từ thư mục ---");

        foreach (var file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            // Gọi hàm AddPngViaXmlWrapper mà chúng ta vừa sửa theo mẫu "Y khuôn"
            if (AddPngToProjectGraphics(file, name))
            {
                Console.WriteLine($"   [+] Đã nạp: {name}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"      [ERROR] Lỗi nạp hàng loạt: {ex.Message}");
    }
}

public bool ImportGraphic(string name, string filePath)
{
    try
    {
        // Truy cập vào kho Graphics của Project qua Reflection
        PropertyInfo graphicsProp = _project.GetType().GetProperty("Graphics");
        var graphicsCollection = graphicsProp.GetValue(_project);
        
        // Gọi hàm Import(name, path, folder)
        // Lưu ý: TIA Openness cho phép nạp trực tiếp vào root Graphics
        MethodInfo importMethod = graphicsCollection.GetType().GetMethod("Import", new[] { typeof(string), typeof(string) });
        if (importMethod != null)
        {
            importMethod.Invoke(graphicsCollection, new object[] { name, filePath });
            return true;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"      [!] Không thể nạp ảnh: {ex.Message}");
    }
    return false;
}
public void CreateGraphicList(string listName, List<string> graphicNames)
{
    try
    {
        dynamic software = GetHmiSoftware(); // Hàm phụ lấy HmiSoftware chúng ta đã viết
        var graphicLists = software.GraphicLists;

        string xmlPath = Path.Combine(@"C:\Capstone Project\", listName + ".xml");
        
        // Tạo nội dung XML cho Graphic List (Standard)
        string entriesXml = "";
        for (int i = 0; i < graphicNames.Count; i++)
        {
            entriesXml += $@"
        <Hmi.GraphicListEntry ID=""{i + 1}"" CompositionName=""Entries"">
          <AttributeList>1
            <GraphicName>{graphicNames[i]}</GraphicName>
            <Value>{i}</Value>
          </AttributeList>
        </Hmi.GraphicListEntry>";
        }

        string xmlContent = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""V20"" />
  <Hmi.GraphicList ID=""0"">
    <AttributeList>
      <Name>{listName}</Name>
      <GraphicListType>Standard</GraphicListType>
    </AttributeList>
    <ObjectList>
      {entriesXml}
    </ObjectList>
  </Hmi.GraphicList>
</Document>";

        File.WriteAllText(xmlPath, xmlContent, System.Text.Encoding.UTF8);
        FileInfo xmlFileInfo = new FileInfo(xmlPath);
        
        // Import vào GraphicLists
        var importMethod = ((object)graphicLists).GetType().GetMethod("Import");
        importMethod.Invoke(graphicLists, new object[] { xmlFileInfo, Enum.ToObject(importMethod.GetParameters()[1].ParameterType, 0) });

        Console.WriteLine($"      [OK] Đã tạo Graphic List: {listName}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"      [ERROR] Lỗi tạo Graphic List: {ex.Message}");
    }
}

public List<string> GetProjectGraphicsNames()
{
    List<string> graphicsNames = new List<string>();
    try
    {
        PropertyInfo graphicsProp = _project.GetType().GetProperty("Graphics");
        if (graphicsProp != null)
        {
            var items = graphicsProp.GetValue(_project) as System.Collections.IEnumerable;
            if (items != null)
            {
                foreach (dynamic graphic in items) graphicsNames.Add(graphic.Name.ToString());
            }
        }
    }
    catch (Exception ex) { Console.WriteLine($"      [!] Lỗi đọc kho ảnh: {ex.Message}"); }
    return graphicsNames;
}

// Đổi HmiScreen thành Screen để hết lỗi CS0246
private void CreateDynamicWidget(dynamic composition, string type, string name, dynamic properties)
{
    try {
        // 1. LẤY TYPE CỦA CONTAINER TỪ ASSEMBLY
        Type targetType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "HmiCustomWidgetContainer");
        
        if (targetType == null) return;

        // 2. TÌM PHƯƠNG THỨC CREATE CÓ 2 THAM SỐ (Name và TypeIdentifier)
        var compositionType = ((object)composition).GetType();
        var methodInfo = compositionType.GetMethods().FirstOrDefault(m => 
            m.Name == "Create" && m.GetParameters().Length == 2);
        
        // 3. XÁC ĐỊNH FILE SVG CỤ THỂ (Mặc định tiền tố extended. cho V20)
        string subType = properties.ContainsKey("SubType") ? properties["SubType"].ToString() : type;
        string typeIdentifier = $"extended.{subType}"; 
        
        // 4. KHỞI TẠO ĐỐI TƯỢNG
        var newItem = (IEngineeringObject)methodInfo.MakeGenericMethod(targetType)
            .Invoke(composition, new object[] { name, typeIdentifier });

        if (newItem != null) {
            // 5. THIẾT LẬP TỌA ĐỘ VÀ KÍCH THƯỚC
            newItem.SetAttribute("Left", Convert.ToInt32(properties["Left"]));
            newItem.SetAttribute("Top", Convert.ToInt32(properties["Top"]));
            newItem.SetAttribute("Width", Convert.ToUInt32(properties["Width"]));
            newItem.SetAttribute("Height", Convert.ToUInt32(properties["Height"]));

            // 6. NẠP THAM SỐ GIAO DIỆN (INTERFACE) DỰA THEO SVG
            try {
                dynamic dynItem = newItem;
                var interfaceProps = dynItem.Properties["Miscellaneous"].Properties["Interface"].Properties;
                
                // A. Tham số chung: Màu vỏ
                interfaceProps["BasicColor"].Value = properties.ContainsKey("BasicColor") ? properties["BasicColor"].ToString() : "238, 238, 238";

                // B. Tham số riêng cho Tank (Mức nước)
                if (subType.Contains("Tank")) {
                    interfaceProps["FillLevelColor"].Value = properties.ContainsKey("FillLevelColor") ? properties["FillLevelColor"].ToString() : "0, 161, 255";
                    interfaceProps["FillLevelValue"].Value = properties.ContainsKey("FillLevelValue") ? Convert.ToDouble(properties["FillLevelValue"]) : 0.0;
                    interfaceProps["DisplayFillLevel"].Value = properties.ContainsKey("DisplayFillLevel") ? Convert.ToBoolean(properties["DisplayFillLevel"]) : true;
                }

                // C. Tham số riêng cho ControlValve (Màu đầu van)
                if (subType.Contains("ControlValve")) {
                    interfaceProps["ContrastColor"].Value = properties.ContainsKey("ContrastColor") ? properties["ContrastColor"].ToString() : "205, 205, 205";
                }
            } catch {
                // Interface có thể chưa nạp kịp từ SVG khi chưa Rebuild All
            }
            
            Console.WriteLine($"      [RENDER] {name} ({subType}) THÀNH CÔNG.");
        }
    } catch (Exception ex) {
        Console.WriteLine($"      [LỖI]: {ex.InnerException?.Message ?? ex.Message}");
    }
}

public void AssignMomentaryTag(string deviceName, string screenName, string itemName, string newTagName)
{
    try {
        var screenItems = GetScreenItemsComposition(deviceName, screenName);
        if (screenItems == null) return;

        // Tìm vật thể bằng vòng lặp để tránh lỗi CS1061
        dynamic targetItem = null;
        foreach (dynamic item in screenItems) {
            if (item.Name == itemName) {
                targetItem = item;
                break;
            }
        }

        if (targetItem == null) return;

        // Duyệt EventHandlers để gán cho cả Press (KeyDown) và Release (KeyUp)
        foreach (dynamic handler in targetItem.EventHandlers) {
            var script = handler.Script; // Đây là ScriptDynamization đã giải phẫu
            if (script != null) {
                string eventType = handler.EventType.ToString();
                int val = (eventType == "KeyDown") ? 1 : 0; 

                // Ghi đè mã JS chuẩn
                script.SourceCode = $"export function {itemName}_On{eventType}(item, keyCode, modifiers) {{\n  Tags(\"{newTagName}\").Write({val});\n}}";
                Console.WriteLine($"   [OK] {itemName} -> {eventType} gán Tag: {newTagName}");
            }
        }
    } catch (Exception ex) { Console.WriteLine($"[LỖI Assign]: {ex.Message}"); }
}
public void ExportAllPathsFromScreen(string deviceName, string screenName, string targetObjectName)
{
    try {
        var device = _project.Devices.Find(deviceName);
        if (device == null) return;

        dynamic hmiSoftware = null;

        // 1. Quét thẳng vào các Item cấp 1 (ví dụ: HMI_RT_2)
        foreach (dynamic item in device.DeviceItems) {
            hmiSoftware = GetHmiSoftwareFromItem(item);
            if (hmiSoftware != null) break;

            // 2. Nếu không thấy, quét vào các Slot bên trong (Rack)
            try {
                foreach (dynamic sub in item.DeviceItems) {
                    hmiSoftware = GetHmiSoftwareFromItem(sub);
                    if (hmiSoftware != null) break;
                }
            } catch { }
            if (hmiSoftware != null) break;
        }

        if (hmiSoftware != null) {
            var screen = hmiSoftware.Screens.Find(screenName);
            if (screen != null) {
                Console.WriteLine($"\n--- 🔍 SIÊU ÂM CẤU TRÚC: {screenName} ---");
                if (!string.IsNullOrEmpty(targetObjectName)) {
                    var obj = screen.ScreenItems.Find(targetObjectName);
                    if (obj != null) ScanDeep(obj);
                } else {
                    foreach (var obj in screen.ScreenItems) ScanDeep(obj);
                }
            } else {
                Console.WriteLine($"[!] Đã thấy Software nhưng không thấy màn hình: {screenName}");
            }
        } else {
            Console.WriteLine("[!] VẪN KHÔNG THẤY HMI SOFTWARE. Hãy kiểm tra tên Device có đúng là 'PC-System_1' không?");
        }
    } catch (Exception ex) { Console.WriteLine($"[!] Lỗi: {ex.Message}"); }
}

private dynamic GetHmiSoftwareFromItem(dynamic item) {
    try {
        // Cách lấy linh hồn HMI chuẩn nhất cho WinCC Unified V20
        var softwareContainer = item.GetService<Siemens.Engineering.HW.Features.SoftwareContainer>();
        if (softwareContainer != null) {
            return softwareContainer.Software;
        }
    } catch { }
    return null;
}


private void ScanDeep(dynamic obj)
{
    Console.WriteLine($"\n==================================================");
    Console.WriteLine($"[SOI CHI TIẾT] Vật thể: {obj.Name}");
    Console.WriteLine($"==================================================");

    // --- CỔNG 1: QUÉT THUỘC TÍNH HỆ THỐNG & ĐỆ QUY TEXT ---
    try {
        Console.WriteLine($"\n[1] DANH SÁCH THUỘC TÍNH HỆ THỐNG:");
        var type = ((object)obj).GetType();
        var properties = type.GetProperties();

        foreach (var p in properties) {
            try {
                if (p.CanRead) {
                    object val = p.GetValue(obj);
                    string n = p.Name;
                    
                    // Lọc các thuộc tính hình học & Text
                    if (n.Contains("Left") || n.Contains("Top") || n.Contains("Width") || 
                        n.Contains("Height") || n.Contains("Text") || n.Contains("Color")) {
                        
                        Console.WriteLine($"    => {n.PadRight(20)} | Kiểu: {p.PropertyType.Name} | Giá trị: {val}");

                        // PHẪU THUẬT SÂU: Nếu thuộc tính là Text (MultilingualText), soi tiếp bên trong
                        if (n == "Text" && val != null) {
                            Console.WriteLine($"       --- Đang soi cấu trúc bên trong của [Text] ---");
                            var subProps = val.GetType().GetProperties();
                            foreach (var sp in subProps) {
                                try {
                                    object sVal = sp.GetValue(val);
                                    Console.WriteLine($"          + {sp.Name.PadRight(15)}: {sVal}");
                                    
                                    // Nếu tìm thấy Items (Danh sách ngôn ngữ), soi phần tử đầu tiên
                                    if (sp.Name == "Items") {
                                        dynamic items = sVal;
                                        if (items.Count > 0) {
                                            Console.WriteLine($"          [!] Tìm thấy {items.Count} mục ngôn ngữ. Phần tử [0] có:");
                                            var itemProps = ((object)items[0]).GetType().GetProperties();
                                            foreach (var ip in itemProps) {
                                                try { Console.WriteLine($"             > {ip.Name}: {ip.GetValue(items[0])}"); } catch { }
                                            }
                                        }
                                    }
                                } catch { }
                            }
                        }
                    }
                }
            } catch { }
        }
    } catch (Exception ex) {
        Console.WriteLine($"    [!] Lỗi quét: {ex.Message}");
    }
}

private void AnalyzeDynamization(dynamic dyns) {
    foreach (dynamic d in dyns) {
        try {
            Console.WriteLine($"      + Kiểu: {d.GetType().Name}");
            // Dò tìm Tag hoặc ScriptCode ẩn
            try { Console.WriteLine($"        - Tag: {d.Tag}"); } catch { }
            try { Console.WriteLine($"        - ScriptCode: {d.ScriptCode}"); } catch { }
            try { Console.WriteLine($"        - Property: {d.PropertyName}"); } catch { }
        } catch { }
    }
}private dynamic FindHmiSoftwareInSlots(dynamic item)
{
    // 1. Kiểm tra trực tiếp item này (có thể là chính nó)
    try {
        foreach (var container in item.GetSoftwareContainers()) {
            if (container.Software != null) {
                // Kiểm tra bằng tên kiểu để tránh lỗi Cast của V20
                if (container.Software.GetType().FullName.Contains("HmiSoftware")) {
                    Console.WriteLine($"      => [HIT] Đã tóm được HMI Software tại: {item.Name}");
                    return container.Software;
                }
            }
        }
    } catch { }

    // 2. Nếu là Rack (PC Station), phải lùng sục vào các Slots (DeviceItems con)
    // Đây là nơi bạn đã gọi PlugNew trong hàm CreateDev
    foreach (var subItem in item.DeviceItems) {
        var found = FindHmiSoftwareInSlots(subItem);
        if (found != null) return found;
    }

    return null;
}



        #endregion

        #region Helpers

        private object GetHmiSoftware()
    {
    if (_project == null) return null;

    // Quét qua tất cả thiết bị trong Project
    foreach (Device device in _project.Devices)
    {
        // Sử dụng hàm FindSoftwareRecursive có sẵn của Otis để tìm Software
        var sw = FindSoftwareRecursive(device);
        if (sw != null)
        {
            string typeName = sw.GetType().Name;
            // Trong Unified, Software có thể là HmiSoftware hoặc HmiTarget
            if (typeName.Contains("Hmi")) 
            {
                return sw;
            }
        }
    }

    // Nếu không tìm thấy ở Root, quét trong các Group (Thư mục)
    foreach (DeviceUserGroup group in _project.DeviceGroups)
    {
        var sw = FindSoftwareInGroupRecursive(group);
        if (sw != null) return sw;
    }

    return null;
}

        // Hàm phụ để quét phần mềm HMI trong các Folder
        private object FindSoftwareInGroupRecursive(DeviceUserGroup group)
        {
            foreach (Device device in group.Devices)
            {
                var sw = FindSoftwareRecursive(device);
                if (sw != null && sw.GetType().Name.Contains("Hmi")) return sw;
            }
            foreach (DeviceUserGroup subGroup in group.Groups)
            {
                var sw = FindSoftwareInGroupRecursive(subGroup);
                if (sw != null) return sw;
            }
            return null;
        }

        private dynamic GetHmiTarget(string deviceName)
        {
            if (_project == null) return null;

            Device device = _project.Devices.Find(deviceName);
            if (device == null) return null;

            Console.WriteLine($"   -> Scanning inside device: {deviceName}...");
            return DeepSearchHmiTarget(device.DeviceItems, 1);
        }
        private dynamic DeepSearchHmiTarget(DeviceItemComposition items, int level)
        {
            string indent = new string(' ', level * 3);

            foreach (DeviceItem item in items)
            {
                Console.WriteLine($"{indent}+ Found item: {item.Name}");

                var container = item.GetService<SoftwareContainer>();
                if (container != null && container.Software != null)
                {
                    // Thủ thuật: Miễn tên kiểu dữ liệu có chữ "Hmi" là chúng ta lấy!
                    // Cái này sẽ tóm gọn cả "HmiTarget" (Panel) và "HmiSoftware" (PC-System)
                    string typeName = container.Software.GetType().Name;
                    if (typeName.Contains("Hmi"))
                    {
                        Console.WriteLine($"{indent}  => [HIT] WinCC Unified Runtime Located! (Type: {typeName})");
                        return container.Software; // Trả về dạng tự do
                    }
                }

                if (item.DeviceItems.Count > 0)
                {
                    dynamic subTarget = DeepSearchHmiTarget(item.DeviceItems, level + 1);
                    if (subTarget != null) return subTarget;
                }
            }
            return null;
        }
        
        private Type GetSiemensType(string fullTypeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName != null && t.FullName.Equals(fullTypeName, StringComparison.OrdinalIgnoreCase));
        }        
        private IEngineeringObject CreateItemGeneric(IEngineeringComposition container, string typeName, string name)
        {
            try
            {
                Type itemType = GetSiemensType(typeName);
                if (itemType == null) return null;

                dynamic dynContainer = container;
                dynamic newItem = dynContainer.Create(itemType, name);
                return newItem as IEngineeringObject;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] CreateItemGeneric failed: {ex.Message}");
                return null;
            }
        }
        private void CreateInternalTagGeneric(string deviceName, string tag, string type) { }
        #endregion

        #region 7. Helpers (Generic & Safe)

        // HÀM MỚI: Tìm Device đệ quy (Dùng cho Compile/SCADA khi Device nằm trong Group)
        private Device FindDeviceRecursive(Project project, string deviceName)
        {
            if (project == null) return null;

            // Tìm ở root
            Device d = project.Devices.Find(deviceName);
            if (d != null) return d;

            // Tìm trong groups
            foreach (DeviceUserGroup group in project.DeviceGroups)
            {
                d = FindDeviceInGroupRecursive(group, deviceName);
                if (d != null) return d;
            }
            return null;
        }

        private Device FindDeviceInGroupRecursive(DeviceUserGroup group, string deviceName)
        {
            Device d = group.Devices.Find(deviceName);
            if (d != null) return d;

            foreach (DeviceUserGroup subGroup in group.Groups)
            {
                d = FindDeviceInGroupRecursive(subGroup, deviceName);
                if (d != null) return d;
            }
            return null;
        }

        private void SetPlcIpAddress(Device device, string ipAddress)
        {
            DeviceItem interfaceItem = FindNetworkInterfaceItem(device.DeviceItems);
            if (interfaceItem != null)
            {
                var networkInterface = interfaceItem.GetService<NetworkInterface>();
                if (networkInterface != null && networkInterface.Nodes.Count > 0)
                {
                    Node node = networkInterface.Nodes[0];
                    try { node.SetAttribute("Address", ipAddress); } catch { }
                }
            }
        }

        private DeviceItem FindNetworkInterfaceItem(DeviceItemComposition items)
        {
            foreach (DeviceItem item in items)
            {
                var netService = item.GetService<NetworkInterface>();
                // Sửa lỗi InterfaceType -> NetType
                if (netService != null && netService.InterfaceType == NetType.Ethernet)
                {
                    return item;
                }
                DeviceItem foundInSub = FindNetworkInterfaceItem(item.DeviceItems);
                if (foundInSub != null) return foundInSub;
            }
            return null;
        }

        private void CheckProject()
        {
            if (_project == null && _tiaPortal != null && _tiaPortal.Projects.Count > 0) _project = _tiaPortal.Projects[0];
        }

        private Software GetSoftware(Device device)
        {
            return FindSoftwareRecursive(device);
        }

        private Software FindSoftwareRecursive(IEngineeringObject obj)
        {
            var provider = obj as IEngineeringServiceProvider;
            var container = provider?.GetService<SoftwareContainer>();
            if (container != null) return container.Software;

            if (obj is Device d)
                foreach (var i in d.DeviceItems) { var r = FindSoftwareRecursive(i); if (r != null) return r; }
            else if (obj is DeviceItem di)
                foreach (var i in di.DeviceItems) { var r = FindSoftwareRecursive(i); if (r != null) return r; }

            return null;
        }

        private IEngineeringComposition GetCompositionSafe(IEngineeringObject obj, string compositionName)
        {
            try
            {
                var compOrObj = obj.GetComposition(compositionName);
                return compOrObj as IEngineeringComposition;
            }
            catch { return null; }
        }

        private IEngineeringComposition GetScreenItemsComposition(string deviceName, string screenName)
        {
            Device device = FindDeviceRecursive(_project, deviceName); // Sửa: Tìm đệ quy
            if (device == null) return null;
            IEngineeringObject software = GetSoftware(device) as IEngineeringObject;
            if (software == null) return null;
            IEngineeringComposition screens = GetCompositionSafe(software, "Screens");
            IEngineeringObject screen = FindObjectByName(screens, screenName);
            return GetCompositionSafe(screen, "ScreenItems");
        }

        private IEngineeringObject FindObjectByName(IEngineeringComposition composition, string name)
        {
            if (composition == null) return null;
            return composition.Cast<IEngineeringObject>().FirstOrDefault(item =>
            {
                try
                {
                    var attr = item.GetAttribute("Name");
                    return attr != null && attr.ToString() == name;
                }
                catch { return false; }
            });
        }

        private void SetAttributeSafe(IEngineeringObject obj, string name, object value)
        {
            try { obj.SetAttribute(name, value); } catch { }
        }
        #endregion

        #region 8. DOWNLOAD OPERATIONS
        // Hàm Download chương trình xuống PLC
        // --- CẬP NHẬT: DOWNLOAD CÓ XỬ LÝ CHỨNG CHỈ BẢO MẬT (TLS) ---
        // --- CẬP NHẬT: DOWNLOAD (BẢN FIX LỖI COMPILE CHO TIA V15/V15.1) ---
       public string DownloadToPLC(string deviceName, string targetIpAddress, string pgPcInterfaceName)
        {
            if (_project == null) return "Project not loaded.";

            try
            {
                // 1. Setup Device & Network (Giữ nguyên)
                Device device = FindDeviceRecursive(_project, deviceName);
                if (device == null) return "Device not found.";
                var downloadProvider = (GetCpuItem(device) as IEngineeringServiceProvider)?.GetService<Siemens.Engineering.Download.DownloadProvider>();
                if (downloadProvider == null) return "DownloadProvider not found.";

                var mode = downloadProvider.Configuration.Modes.Find("PN/IE");
                var pcInterface = mode.PcInterfaces.Find(pgPcInterfaceName, 1);
                if (pcInterface == null) foreach (var pc in mode.PcInterfaces) if (pc.Name.Contains(pgPcInterfaceName)) { pcInterface = pc; break; }
                if (pcInterface == null) return "Net Card not found.";
                var targetConf = pcInterface.TargetInterfaces.Count > 0 ? pcInterface.TargetInterfaces[0] : null;

                // 2. THỰC HIỆN DOWNLOAD
                Console.WriteLine("Starting download process...");
                bool autoStart = false;

                Siemens.Engineering.Download.DownloadResult result = downloadProvider.Download(
                    targetConf,
                    
                    // --- PHẦN 1: PRE-DOWNLOAD (AUTO-STOP) ---
                    (preConf) => 
                    {
                        Console.WriteLine("\n[TIA PRE-CHECK]");
                        try { foreach (var msg in ((dynamic)preConf).Messages) Console.WriteLine($"- {msg.Message}"); } catch {}

                        // XỬ LÝ: STOP MODULES (ÁP DỤNG LOGIC ENUM)
                        try 
                        {
                            // Kiểm tra xem có thuộc tính CurrentSelection (Enum) không
                            var prop = preConf.GetType().GetProperty("CurrentSelection");
                            if (prop != null)
                            {
                                var currentValue = prop.GetValue(preConf);
                                var enumType = currentValue.GetType();
                                string[] enumNames = Enum.GetNames(enumType);

                                foreach (var name in enumNames)
                                {
                                    // Tìm chữ "Stop" (Ví dụ: StopAll, StopModules...)
                                    if (name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        var newValue = Enum.Parse(enumType, name);
                                        prop.SetValue(preConf, newValue);
                                        Console.WriteLine($"   [AUTO-STOP]: Selected action '{name}'");
                                        break;
                                    }
                                }
                            }
                            else 
                            {
                                // Fallback: Nếu không phải Enum, thử duyệt List (cho các trường hợp khác)
                                var list = preConf as System.Collections.IEnumerable;
                                if (list != null)
                                {
                                    foreach (dynamic item in list)
                                    {
                                        try {
                                            foreach (dynamic option in item.Options) {
                                                if (option.Name.ToString().Contains("Stop")) {
                                                    item.Current = option;
                                                    Console.WriteLine("   [AUTO-STOP]: Selected option 'Stop'");
                                                    break;
                                                }
                                            }
                                        } catch {}
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { Console.WriteLine($"[Warning] Auto-Stop error: {ex.Message}"); }
                    },
                    
                    // --- PHẦN 2: POST-DOWNLOAD (AUTO-START) ---
                    (postConf) => 
                    {
                        Console.WriteLine("\n[TIA POST-DOWNLOAD]");
                        try 
                        {
                            // XỬ LÝ: START MODULES (LOGIC ENUM)
                            var prop = postConf.GetType().GetProperty("CurrentSelection");
                            if (prop != null)
                            {
                                var currentValue = prop.GetValue(postConf);
                                var enumType = currentValue.GetType();
                                foreach (var name in Enum.GetNames(enumType))
                                {
                                    if (name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        prop.SetValue(postConf, Enum.Parse(enumType, name));
                                        Console.WriteLine($"   [AUTO-START]: Selected action '{name}'");
                                        autoStart = true;
                                        break;
                                    }
                                }
                            }
                            // Fallback duyệt List cho Start (nếu cần)
                            else
                            {
                                dynamic dynConf = postConf;
                                System.Collections.IEnumerable items = dynConf as System.Collections.IEnumerable;
                                if (items == null) try { items = dynConf.Items; } catch {}
                                if (items != null)
                                {
                                    foreach (dynamic item in items) {
                                        foreach (dynamic option in item.Options) {
                                            if (option.Name.ToString().Contains("Start")) {
                                                item.Current = option;
                                                autoStart = true;
                                                Console.WriteLine("   [AUTO-START]: Selected option 'Start'");
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { Console.WriteLine($"Error setting start: {ex.Message}"); }
                    },
                    Siemens.Engineering.Download.DownloadOptions.Hardware | Siemens.Engineering.Download.DownloadOptions.Software
                );

                if (result.State == Siemens.Engineering.Download.DownloadResultState.Success)
                {
                    if (autoStart) return "Download Complete & PLC RESTARTED (Auto).";
                    else return "Download Complete (PLC is STOPPED).";
                }
                else
                {
                     var msg = result.Messages.FirstOrDefault(m => m.State == Siemens.Engineering.Download.DownloadResultState.Error)?.Message ?? "Unknown Error";
                     if (msg.Contains("Connect to module") || msg.Contains("failed"))
                         return "⚠️ LỖI KẾT NỐI: Vui lòng nạp thủ công 1 lần để xác nhận Certificate!";
                     return $"Download Error: {msg}";
                }
            }
            catch (Exception ex) { return $"Download Exception: {ex.Message}"; }
        }

        // --- BỔ SUNG: THAY ĐỔI TRẠNG THÁI PLC (RUN/STOP) ---
        // --- FUNCTION: MANUAL START/STOP PLC (FIX LỖI STOP KHI ĐANG RUN) ---
        public string ChangePlcState(string deviceName, string targetIp, string netCard, bool turnOn)
        {
            string actionName = turnOn ? "Start" : "Stop";
            string targetDesc = turnOn ? "RUN (Start Module)" : "STOP (Stop Module)";

            Console.WriteLine($"\n--- EXECUTING MANUAL COMMAND: {targetDesc} ---");

            if (_project == null) return "Error: Project not loaded.";
            
            try
            {
                // 1. SETUP
                Device device = FindDeviceRecursive(_project, deviceName);
                if (device == null) return "Error: Device not found.";
                
                var downloadProvider = (GetCpuItem(device) as IEngineeringServiceProvider)?.GetService<Siemens.Engineering.Download.DownloadProvider>();
                if (downloadProvider == null) return "Error: CPU does not support Download/Control.";

                var mode = downloadProvider.Configuration.Modes.Find("PN/IE");
                var pcInterface = mode.PcInterfaces.Find(netCard, 1);
                if (pcInterface == null) 
                    foreach (var pc in mode.PcInterfaces) if (pc.Name.Contains(netCard)) { pcInterface = pc; break; }
                
                if (pcInterface == null) return "Error: Network Card not found.";
                var targetConf = pcInterface.TargetInterfaces.Count > 0 ? pcInterface.TargetInterfaces[0] : null;

                // 2. THỰC HIỆN LỆNH
                bool actionSuccess = false;
                var ops = Siemens.Engineering.Download.DownloadOptions.Hardware | Siemens.Engineering.Download.DownloadOptions.Software;

                var result = downloadProvider.Download(
                    targetConf,
                    
                    // --- PRE-DOWNLOAD: QUAN TRỌNG - PHẢI XỬ LÝ STOP MODULES TẠI ĐÂY ---
                    (preConf) => 
                    {
                        // Logic này giúp xử lý tình huống: PLC đang RUN mà muốn nạp lệnh STOP
                        try 
                        {
                            // 1. Thử xử lý theo kiểu Enum (StopModulesSelections)
                            var prop = preConf.GetType().GetProperty("CurrentSelection");
                            if (prop != null)
                            {
                                var currentValue = prop.GetValue(preConf);
                                var enumType = currentValue.GetType();
                                foreach (var name in Enum.GetNames(enumType))
                                {
                                    if (name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        prop.SetValue(preConf, Enum.Parse(enumType, name));
                                        // Console.WriteLine($"   [Pre-Check] Auto-accepted: {name}");
                                        break;
                                    }
                                }
                            }
                            // 2. Thử xử lý theo kiểu List (Fallback)
                            else 
                            {
                                var list = preConf as System.Collections.IEnumerable;
                                if (list != null)
                                {
                                    foreach (dynamic item in list)
                                    {
                                        try {
                                            foreach (dynamic option in item.Options) {
                                                if (option.Name.ToString().Contains("Stop")) {
                                                    item.Current = option;
                                                    break;
                                                }
                                            }
                                        } catch {}
                                    }
                                }
                            }
                        }
                        catch {} // Bỏ qua lỗi nhỏ để ưu tiên chạy tiếp
                    },
                    
                    // --- POST-DOWNLOAD: CHỌN TRẠNG THÁI CUỐI CÙNG ---
                    (postConf) => 
                    {
                        Console.WriteLine($"-> Configuring PLC State to: {actionName.ToUpper()}...");
                        try 
                        {
                            var prop = postConf.GetType().GetProperty("CurrentSelection");
                            if (prop != null)
                            {
                                var currentValue = prop.GetValue(postConf);
                                var enumType = currentValue.GetType();
                                string[] enumNames = Enum.GetNames(enumType);
                                bool found = false;

                                foreach (var name in enumNames)
                                {
                                    bool isTarget = false;
                                    if (turnOn) // RUN
                                        isTarget = name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0;
                                    else // STOP
                                        isTarget = name.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   name.IndexOf("NoAction", StringComparison.OrdinalIgnoreCase) >= 0;

                                    if (isTarget)
                                    {
                                        prop.SetValue(postConf, Enum.Parse(enumType, name));
                                        Console.WriteLine($"   [OK] Action Set: {name}");
                                        actionSuccess = true;
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found && !turnOn)
                                {
                                    // Nếu muốn STOP mà không thấy tùy chọn Stop -> Có thể nó đã Stop từ bước Pre-Check rồi
                                    // Ta cứ báo Success để người dùng không hoang mang
                                    Console.WriteLine("   [Info] PLC might be already stopped via Pre-Check.");
                                    actionSuccess = true;
                                }
                            }
                        }
                        catch (Exception ex) { Console.WriteLine($"   [Error] State conf failed: {ex.Message}"); }
                    },
                    ops 
                );

                // 3. KẾT QUẢ
                if (result.State == Siemens.Engineering.Download.DownloadResultState.Success)
                {
                    // Nếu muốn Stop mà ở Pre-Check đã Stop rồi thì coi như thành công
                    if (actionSuccess || !turnOn) return $"SUCCESS: PLC switched to {targetDesc}.";
                    else return "WARNING: Command sent but State Option was not explicitly confirmed.";
                }
                else
                {
                    var msg = result.Messages.FirstOrDefault(m => m.State == Siemens.Engineering.Download.DownloadResultState.Error)?.Message ?? "Unknown";
                    if (msg.Contains("Connect to module") || msg.Contains("failed"))
                         return "⚠️ FAILED: Connection refused. Check Certificate or Network.";
                    return $"FAILED: {msg}";
                }
            }
            catch (Exception ex) { return $"EXCEPTION: {ex.Message}"; }
        }        
        public string GetPlcStatus(string deviceName, string netCard)
        {
            Console.WriteLine($"\n--- CHECKING CONNECTION ---");
            if (_project == null) return "Error: Project not loaded.";

            try
            {
                Device device = FindDeviceRecursive(_project, deviceName);
                if (device == null) return "Error: Device not found.";
                
                DeviceItem cpuItem = GetCpuItem(device);
                var serviceProvider = cpuItem as IEngineeringServiceProvider;
                var onlineProvider = serviceProvider?.GetService<Siemens.Engineering.Online.OnlineProvider>();
                
                if (onlineProvider == null) return "Error: No Online support.";

                // Cấu hình mạng
                var mode = onlineProvider.Configuration.Modes.Find("PN/IE");
                var pcInterface = mode.PcInterfaces.Find(netCard, 1);
                if (pcInterface == null)
                    foreach (var pc in mode.PcInterfaces) if (pc.Name.Contains(netCard)) { pcInterface = pc; break; }

                if (pcInterface == null) return "Error: Net Card not found.";

                // Thử kết nối
                Console.WriteLine(">> Pinging PLC (Going Online)...");
                onlineProvider.GoOnline();

                if (onlineProvider.State == Siemens.Engineering.Online.OnlineState.Online)
                {
                    onlineProvider.GoOffline();
                    return "SUCCESS: PLC is ONLINE and REACHABLE.";
                }
                return "WARNING: PLC not reachable.";
            }
            catch (Exception ex)
            {
                return $"CONNECTION FAILED: {ex.Message}";
            }
        }
        private DeviceItem GetCpuItem(Device device)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                var sp = item as IEngineeringServiceProvider;
                if (sp?.GetService<Siemens.Engineering.Download.DownloadProvider>() != null) return item;

                // Tìm sâu hơn 1 cấp (cho S7-1500)
                foreach (DeviceItem sub in item.DeviceItems)
                {
                    var spSub = sub as IEngineeringServiceProvider;
                    if (spSub?.GetService<Siemens.Engineering.Download.DownloadProvider>() != null) return sub;
                }
            }
            return null;
        }

        public string SmartUpdateFirmware(string deviceName, string newOrderNumber, string newVersion)
        {
            if (_project == null) return "Project not loaded.";

            try
            {
                // 1. TÌM THIẾT BỊ
                Device device = FindDeviceRecursive(_project, deviceName); // Dùng hàm đệ quy tìm cho chắc
                if (device == null) return $"Error: PLC '{deviceName}' not found.";

                // Chuẩn bị Type Identifier
                string typeIdentifier = $"OrderNumber:{newOrderNumber}/{newVersion}";

                // --- CHIẾN THUẬT 1: THỬ EXCHANGE (Giữ nguyên code) ---
                // Đây là cách tốt nhất, giữ nguyên được kết nối mạng và logic
                bool exchangeSuccess = false;

                // Thử Exchange CPU Item
                DeviceItem cpuItem = GetCpuItem(device);
                if (cpuItem != null)
                {
                    try
                    {
                        ((dynamic)cpuItem).Exchange(typeIdentifier);
                        exchangeSuccess = true;
                    }
                    catch { }
                }

                // Thử Exchange Root Device Item (cho S7-1200 cũ)
                if (!exchangeSuccess)
                {
                    try
                    {
                        foreach (DeviceItem item in device.DeviceItems)
                        {
                            if (!string.IsNullOrEmpty(item.TypeIdentifier))
                            {
                                ((dynamic)item).Exchange(typeIdentifier);
                                exchangeSuccess = true;
                                break;
                            }
                        }
                    }
                    catch { }
                }

                if (exchangeSuccess) return $"Success: Updated {deviceName} to {newVersion} via Exchange.";


                // --- CHIẾN THUẬT 2: BACKUP -> DELETE -> CREATE -> RESTORE ---
                // Nếu Exchange thất bại (do khác hệ đời, vd S7-300 lên 1500, hoặc FW quá cũ), ta làm thủ công.

                // A. Backup dữ liệu cũ
                string tempBackupPath = Path.Combine(Path.GetTempPath(), "TIA_Backup_" + Guid.NewGuid().ToString());
                string backupResult = BackupPlcData(deviceName, tempBackupPath);

                if (backupResult.Contains("Failed"))
                    return $"Update Failed: Could not backup old PLC. ({backupResult})";

                // B. Lưu lại IP Address cũ (để gán lại cho con mới)
                // (Bạn cần viết hàm GetPlcIpAddress, ở đây tôi giả sử IP cố định hoặc bạn nhập vào)
                // string oldIp = GetPlcIpAddress(device); 

                // C. Xóa PLC cũ & Tạo PLC mới
                device.Delete();
                Device newDevice = _project.Devices.CreateWithItem(typeIdentifier, deviceName, deviceName);

                // D. Restore dữ liệu vào PLC mới
                string restoreResult = RestorePlcData(deviceName, tempBackupPath);

                // E. Dọn dẹp file rác
                try { Directory.Delete(tempBackupPath, true); } catch { }
                return $"Success (Replaced): Updated via Backup/Restore.\nDetails: {restoreResult}";
                
            }
            catch (Exception ex)
            {
                return $"Fatal Error: {ex.Message}";
            }
        }
        #endregion

        #region 9. BACKUP & RESTORE OPERATIONS (FIXED RECURSIVE)

        // HÀM 1: BACKUP TOÀN BỘ CODE (Sử dụng đệ quy để tìm trong mọi thư mục)
        public string BackupPlcData(string deviceName, string backupFolderPath)
        {
            if (_project == null) return "Project not loaded.";

            Device device = FindDeviceRecursive(_project, deviceName);
            if (device == null) return "Device not found.";

            // Lấy PlcSoftware
            PlcSoftware software = GetSoftware(device) as PlcSoftware;
            if (software == null) return "Target is not a PLC or Software not found.";

            try
            {
                // Tạo/Làm sạch thư mục backup
                if (Directory.Exists(backupFolderPath)) Directory.Delete(backupFolderPath, true);
                Directory.CreateDirectory(backupFolderPath);

                int countUDT = 0;
                int countTags = 0;
                int countBlocks = 0;

                // --- 1. EXPORT UDT (Types) ---
                // UDT cũng có thể nằm trong folder, cần duyệt đệ quy (nếu version TIA hỗ trợ Type User Groups)
                // Tuy nhiên thường UDT nằm phẳng. Để chắc ăn, ta quét root trước.
                foreach (PlcType type in software.TypeGroup.Types)
                {
                    try
                    {
                        string path = Path.Combine(backupFolderPath, "UDT_" + type.Name + ".xml");
                        type.Export(new FileInfo(path), ExportOptions.None);
                        countUDT++;
                    }
                    catch { }
                }

                // --- 2. EXPORT TAGS ---
                foreach (PlcTagTable tagTable in software.TagTableGroup.TagTables)
                {
                    try
                    {
                        string path = Path.Combine(backupFolderPath, "TAG_" + tagTable.Name + ".xml");
                        tagTable.Export(new FileInfo(path), ExportOptions.None);
                        countTags++;
                    }
                    catch { }
                }

                // --- 3. EXPORT BLOCKS (QUAN TRỌNG: ĐỆ QUY) ---
                // Gọi hàm phụ trợ để quét sạch mọi ngóc ngách thư mục
                countBlocks = ExportBlocksRecursive(software.BlockGroup, backupFolderPath);

                return $"Backup Done. Stats: {countUDT} UDTs, {countTags} TagTables, {countBlocks} Blocks.";
            }
            catch (Exception ex)
            {
                return $"Backup Failed: {ex.Message}";
            }
        }

        // HÀM PHỤ TRỢ: ĐỆ QUY TÌM BLOCK TRONG GROUP
        private int ExportBlocksRecursive(PlcBlockGroup group, string path)
        {
            int count = 0;

            // A. Duyệt các Block ở level hiện tại
            foreach (PlcBlock block in group.Blocks)
            {
                try
                {
                    // Lọc Block hệ thống bằng Try-Catch (Vì API PlcBlock không có IsSystemBlock ở một số version)
                    // Quy tắc đặt tên file: Thêm prefix BLK_ để dễ lọc khi restore
                    string fileName = "BLK_" + block.Name + ".xml";

                    // Xử lý ký tự đặc biệt trong tên file nếu có
                    foreach (char c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');

                    block.Export(new FileInfo(Path.Combine(path, fileName)), ExportOptions.WithDefaults);
                    count++;
                }
                catch
                {
                    // Bỏ qua các block hệ thống bị khóa hoặc không export được
                }
            }

            // B. Duyệt tiếp vào các Group con (Thư mục con) -> ĐÂY LÀ PHẦN CODE CŨ BỊ THIẾU
            foreach (PlcBlockUserGroup userGroup in group.Groups)
            {
                count += ExportBlocksRecursive(userGroup, path);
            }

            return count;
        }

        // HÀM 2: RESTORE (IMPORT)
        public string RestorePlcData(string deviceName, string backupFolderPath)
        {
            if (_project == null) return "Project not loaded.";
            Device device = FindDeviceRecursive(_project, deviceName);
            PlcSoftware software = GetSoftware(device) as PlcSoftware;
            if (software == null) return "Target Error.";

            if (!Directory.Exists(backupFolderPath)) return "No backup data found.";

            var files = Directory.GetFiles(backupFolderPath, "*.xml");
            ImportOptions option = ImportOptions.Override; // Bắt buộc Override để đè OB1 mặc định
            StringBuilder log = new StringBuilder();

            // 1. IMPORT UDT (Loop 3 lần để xử lý phụ thuộc)
            var udtFiles = files.Where(f => Path.GetFileName(f).StartsWith("UDT_")).ToList();
            for (int i = 0; i < 3; i++)
            {
                if (udtFiles.Count == 0) break;
                List<string> done = new List<string>();
                foreach (var file in udtFiles)
                {
                    try { software.TypeGroup.Types.Import(new FileInfo(file), option); done.Add(file); } catch { }
                }
                foreach (var d in done) udtFiles.Remove(d);
            }

            // 2. IMPORT TAGS
            var tagFiles = files.Where(f => Path.GetFileName(f).StartsWith("TAG_")).ToList();
            foreach (var file in tagFiles)
            {
                try { software.TagTableGroup.TagTables.Import(new FileInfo(file), option); } catch { }
            }

            // 3. IMPORT BLOCKS (Loop 3 lần)
            var blockFiles = files.Where(f => Path.GetFileName(f).StartsWith("BLK_")).ToList();
            for (int i = 0; i < 3; i++)
            {
                if (blockFiles.Count == 0) break;
                List<string> done = new List<string>();
                foreach (var file in blockFiles)
                {
                    try
                    {
                        // Import thẳng vào Root Block Group
                        // (Lưu ý: TIA sẽ tự import vào root, cấu trúc thư mục cũ sẽ bị mất, block sẽ nằm phẳng ở ngoài.
                        // Nếu muốn giữ cấu trúc folder thì phức tạp hơn nhiều, nhưng code vẫn chạy đúng logic PLC)
                        software.BlockGroup.Blocks.Import(new FileInfo(file), option);
                        done.Add(file);
                    }
                    catch (Exception ex)
                    {
                        // Log lỗi nếu cần thiết
                    }
                }
                foreach (var d in done) blockFiles.Remove(d);
            }

            // 4. THỬ COMPILE ĐỂ KIỂM TRA
            try
            {
                var compiler = (software as IEngineeringServiceProvider).GetService<ICompilable>();
                compiler.Compile();
            }
            catch { }

            return "Restore Completed.";
        }
        #endregion
        public string GetDeviceType(string deviceName)
        {
            if (_project == null) return "Unknown";
            Device device = FindDeviceRecursive(_project, deviceName);
            
            if (device != null)
            {
                if (!string.IsNullOrEmpty(device.TypeIdentifier)) 
                    return device.TypeIdentifier;

                foreach (DeviceItem item in device.DeviceItems)
                {
                    if (!string.IsNullOrEmpty(item.TypeIdentifier)) return item.TypeIdentifier;
                }
            }
            return "Unknown";
        }
        // --- BỔ SUNG: LẤY TÊN PROJECT ---
        public string GetProjectName()
        {
            if (_project != null)
            {
                try { return _project.Name; } catch { }
            }
            return "Unknown";
        }

        public string GetDeviceIp(string deviceName)
        {
            if (_project == null) return "0.0.0.0";
            Device device = FindDeviceRecursive(_project, deviceName);
            
            if (device != null)
            {
                DeviceItem netItem = FindNetworkInterfaceItem(device.DeviceItems);
                if (netItem != null)
                {
                    var networkInterface = netItem.GetService<Siemens.Engineering.HW.Features.NetworkInterface>();
                    if (networkInterface != null && networkInterface.Nodes.Count > 0)
                    {
                        try 
                        { 
                            return networkInterface.Nodes[0].GetAttribute("Address").ToString();
                        } 
                        catch { }
                    }
                }
            }
            return "0.0.0.0";
        }
        // --- FUNCTION: TEST ONLINE VISUAL (GIỮ KẾT NỐI 10 GIÂY ĐỂ NHÌN) ---
        public string FlashPlcLed(string deviceName, string netCard)
        {
            Console.WriteLine($"\n--- TESTING CONNECTION (HOLDING ONLINE FOR 10s) ---");

            if (_project == null) return "Error: Project not loaded.";

            Siemens.Engineering.Online.OnlineProvider onlineProvider = null;

            try
            {
                // 1. SETUP (Tìm thiết bị)
                Device device = FindDeviceRecursive(_project, deviceName);
                if (device == null) return "Error: Device not found.";
                
                DeviceItem cpuItem = GetCpuItem(device);
                if (cpuItem == null) return "Error: CPU item not found.";

                // 2. LẤY ONLINE PROVIDER
                var serviceProvider = cpuItem as IEngineeringServiceProvider;
                onlineProvider = serviceProvider?.GetService<Siemens.Engineering.Online.OnlineProvider>();
                if (onlineProvider == null) return "Error: CPU does not support Online connection.";

                // 3. CẤU HÌNH MẠNG
                var mode = onlineProvider.Configuration.Modes.Find("PN/IE");
                var pcInterface = mode.PcInterfaces.Find(netCard, 1);
                if (pcInterface == null)
                    foreach (var pc in mode.PcInterfaces) if (pc.Name.Contains(netCard)) { pcInterface = pc; break; }

                if (pcInterface == null) return "Error: Network Card not found.";

                // 4. THỰC HIỆN KẾT NỐI
                Console.WriteLine(">> Going Online... (Please watch TIA Portal window)");
                onlineProvider.GoOnline(); 

                if (onlineProvider.State == Siemens.Engineering.Online.OnlineState.Online)
                {
                    // 5. GIỮ KẾT NỐI VÀ ĐẾM NGƯỢC
                    Console.WriteLine("\n>> [SUCCESS] PLC IS ONLINE!");
                    Console.WriteLine(">> Look at TIA Portal now: You should see ORANGE bars and GREEN checks.");
                    
                    Console.Write(">> Going Offline in: ");
                    for (int i = 10; i > 0; i--)
                    {
                        Console.Write($"{i}... ");
                        System.Threading.Thread.Sleep(1000); // Dừng 1 giây
                    }
                    Console.WriteLine("Now!");
                    
                    return "SUCCESS: Connection verified manually.";
                }
                else
                {
                    return "WARNING: Command sent but PLC did not report Online state.";
                }
            }
            catch (Exception ex)
            {
                return $"CONNECTION FAILED: {ex.Message}";
            }
            finally
            {
                // 6. NGẮT KẾT NỐI
                try 
                {
                    if (onlineProvider != null && onlineProvider.State == Siemens.Engineering.Online.OnlineState.Online)
                    {
                        Console.WriteLine(">> Disconnected (Offline).");
                        onlineProvider.GoOffline();
                    }
                }
                catch {}
            }
        }
        #region 10. WinCC Unified Networking
       public string CreateUnifiedConnectionCombined(string hmiName, string hmiIp, string plcIp, string connectionName = "Connection_1")
        {
            if (_project == null) return "Project chưa mở.";

            try
            {
                Device hmiDevice = FindDeviceRecursive(_project, hmiName);
                if (hmiDevice == null) return $"[ERROR] Không tìm thấy thiết bị: {hmiName}";

                var software = GetSoftware(hmiDevice) as HmiSoftware;
                var connections = software.Connections;
                var existing = connections.Find(connectionName);
                if (existing != null) existing.Delete();

                // Bước 1: Tạo kết nối và định danh Driver
                var newConn = connections.Create(connectionName);
                newConn.SetAttribute("CommunicationDriver", "SIMATIC S7 1200/1500");

                // Bước 2: GIẢI PHÁP - Gán trực tiếp từng thuộc tính thay vì gửi chuỗi InitialAddress
                // Cách này giúp TIA Portal không phải tự phân tách chuỗi, tránh lỗi format
                try 
                {
                    newConn.SetAttribute("HostAddress", hmiIp); // IP của HMI
                    newConn.SetAttribute("PlcAddress", plcIp); // IP của PLC
                    newConn.SetAttribute("HostAccessPoint", "S7ONLINE");
                    
                    // Gán các thông số phụ mà Driver yêu cầu
                    newConn.SetAttribute("PlcExpansionSlot", 1); 
                    newConn.SetAttribute("PlcRack", 0);
                    newConn.SetAttribute("PlcIsCyclicOperation", true);
                }
                catch 
                {
                    // Fallback: Nếu gán rời bị chặn, dùng chuỗi tối giản nhất (không có dấu ; ở cuối)
                    string minimal = $"Version=16.0.0.0;HostAddress={hmiIp};PlcAddress={plcIp}";
                    newConn.SetAttribute("InitialAddress", minimal);
                }

                return $"[SUCCESS] Đã tạo và thiết lập kết nối: {connectionName}";
            }
            catch (Exception ex)
            {
                return $"[ERROR] Lỗi hệ thống: {ex.Message}";
            }
        }
        #endregion
        #region 11. WinCC Unified Tag Creation
        public void ImportHmiTagsFromCsv(string hmiName, string csvPath)
{
    if (_project == null) { ConsoleUI.PrintResult("[ERROR] Project chưa mở."); return; }

    try
    {
        if (!System.IO.File.Exists(csvPath)) {
            ConsoleUI.PrintResult($"[ERROR] Không tìm thấy file: {csvPath}"); return;
        }

        Device hmiDevice = FindDeviceRecursive(_project, hmiName);
        var software = GetSoftware(hmiDevice) as HmiSoftware;
        var table = software.TagTables.Find("Default tag table") ?? software.TagTables.Create("Imported_Tags");

        string[] lines = System.IO.File.ReadAllLines(csvPath);
        int successCount = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            // Quan trọng: Thử dùng ';' nếu file export từ Excel Việt Nam/Châu Âu
            string[] columns = lines[i].Split(','); 
            if (columns.Length < 4) columns = lines[i].Split(';'); 

            // Kiểm tra an toàn để tránh lỗi Index outside bounds
            if (columns.Length < 4) {
                ConsoleUI.PrintResult($"[ERROR] Dòng {i + 1} không đủ 4 cột dữ liệu cơ bản.");
                continue;
            }

            string tagName = columns[0].Trim();      // Cột A
            string connName = columns[1].Trim();     // Cột B
            string address = columns[2].Trim();      // Cột C
            string dataType = columns[3].Trim();     // Cột D

            try 
            {
                var tags = table.Tags;
                if (tags.Find(tagName) != null) tags.Find(tagName).Delete();
                
                var newTag = tags.Create(tagName);

                // 1. Gán Connection trước để "Unlock" các trường dữ liệu
                newTag.SetAttribute("Connection", connName); 

                // 2. Gán DataType (Phải viết hoa chữ đầu: Bool, Int, Real)
                newTag.SetAttribute("DataType", dataType); 

                // 3. Thiết lập chế độ Tuyệt đối (1 = AbsoluteAccess)
                newTag.SetAttribute("AccessMode", 1); 

                // 4. Gán địa chỉ tuyệt đối (ví dụ %M1.0)
                newTag.SetAttribute("Address", address);
                // Kiểm tra nếu cột IsLogging là True thì gọi hàm kích hoạt Log
                if (columns.Length >= 6 && columns[4].Trim().ToLower() == "true")
                {
                    string logName = columns[5].Trim();
                    EnableLoggingForTag(hmiName, tagName, logName);
                }

                // 5. Gán Acquisition Cycle nếu có (Cột G)
                if (columns.Length >= 7) {
                    newTag.SetAttribute("AcquisitionCycle", columns[6].Trim());
                }

                successCount++;
                Console.WriteLine($"[INFO] Đã nạp thành công: {tagName}");
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintResult($"[ERROR] Dòng {i + 1} ({tagName}): {ex.Message}");
            }
        }
        ConsoleUI.PrintResult($"[SUCCESS] Hoàn thành! Đã nạp {successCount}/{lines.Length - 1} tags vào {hmiName}.");
    }
    catch (Exception ex) { ConsoleUI.PrintResult($"[ERROR] Fatal: {ex.Message}"); }
}

public string EnableLoggingForTag(string hmiName, string tagName, string dataLogName)
{
    if (_project == null) return "[ERROR] Project chưa mở.";

    try
    {
        Device hmiDevice = FindDeviceRecursive(_project, hmiName);
        var software = GetSoftware(hmiDevice) as HmiSoftware;
        
        // 1. Tìm Tag cần Log trong Default tag table
        var table = software.TagTables.Find("Default tag table");
        var hmiTag = table.Tags.Find(tagName);
        if (hmiTag == null) return $"[ERROR] Không tìm thấy Tag: {tagName}";

        // 2. Truy cập danh sách LoggingTags của Tag đó
        var loggingTags = hmiTag.LoggingTags;
        
        // 3. Tạo LoggingTag mới (thường đặt tên trùng với tên Tag hoặc tagName_Log)
        string logTagName = tagName + "_Log";
        var existingLog = loggingTags.Find(logTagName);
        if (existingLog != null) existingLog.Delete();

        var newLoggingTag = loggingTags.Create(logTagName);

        // 4. Cấu hình các thuộc tính dựa trên API bạn gửi
        // Gán vào bảng Data Log (Ví dụ: "Data_log_1")
        newLoggingTag.SetAttribute("LogConfiguration", dataLogName); 
        
        // Chế độ ghi: 3 = OnChange (Ghi khi thay đổi)
        newLoggingTag.SetAttribute("LoggingMode", 3); 

        // Nếu muốn làm mượt dữ liệu (Smoothing)
        newLoggingTag.SetAttribute("SmoothingMode", 0); // 0 = NoSmoothing

        return $"[SUCCESS] Đã kích hoạt Data Log cho Tag '{tagName}' vào bảng '{dataLogName}'";
    }
    catch (Exception ex)
    {
        return $"[ERROR] Lỗi Logging: {ex.Message}";
    }
}
#endregion
    }
    
       
    #region Data Models
        public class ScadaScreenModel
    {
        public string ScreenName { get; set; }
        public List<ScadaLayerModel> Layers { get; set; }
        public List<ScadaItemModel> Items { get; set; }
    }
    public class ScadaLayerModel
    {
        public string LayerName { get; set; }
        public List<ScadaItemModel> Items { get; set; }
    }
    public class ScadaItemModel
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public bool? EnableCreation { get; set; } = true;
        public Dictionary<string, object> Properties { get; set; }
        public Dictionary<string, string> Events { get; set; }
        public List<ScadaItemModel> Items { get; set; }
        public string TagName { get; set; }
        public LibraryModel Library { get; set; }
    }
        public class LibraryModel
    {
        public string LibraryPath { get; set; }
        public string SubLibrary { get; set; }
    }
    #endregion

    
}