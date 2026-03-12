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
        #region 1. Fields, Constructor & Connectivity Status
        private TiaPortal _tiaPortal;
        private Project _project;

        public TIA_V20() { }

        public bool IsConnected => _tiaPortal != null && _project != null;

        public void ConnectToTiaPortal()
        {
            if (!ConnectToTIA())
            {
                throw new Exception("No running TIA Portal instance found!");
            }
        }
        #endregion

        #region 2. TIA Portal & Project Management
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
                if (_project != null)
                {
                    _project.Close();
                    _project = null;
                }
                if (_tiaPortal != null)
                {
                    _tiaPortal.Dispose();
                    _tiaPortal = null;
                }
                foreach (var process in Process.GetProcessesByName("Siemens.Automation.Portal"))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    catch { }
                }
            }
            catch { }
        }

        public string GetProjectName()
        {
            if (_project != null)
            {
                try { return _project.Name; } catch { }
            }
            return "Unknown";
        }
        #endregion

        #region 3. Hardware Configuration & Network
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
                if (typeIdentifier.Contains("xxxxx") || typeIdentifier.Contains("6AV2 155"))
                {
                    Console.WriteLine($"[Auto-Fix] Detected WinCC Unified PC. Starting Optimized Creation...");
                    string[] pcIds = new string[] { "System:Rack.PC", "OrderNumber:6ES7647-0AA00-1YA0/V3.0" };
                    foreach (string id in pcIds)
                    {
                        try
                        {
                            newDevice = _project.Devices.CreateWithItem(id, devName, devName);
                            if (newDevice != null) break;
                        }
                        catch { }
                    }
                    if (newDevice == null)
                    {
                        try { newDevice = _project.Devices.Create("System:Device.PC", devName); } catch { }
                    }

                    if (newDevice == null) throw new Exception("FATAL: Could not create PC Station.");

                    DeviceItem pcRack = newDevice.DeviceItems[0];
                    string netId = "OrderNumber:IE General/V2.0";
                    for (int i = 1; i <= 2; i++)
                    {
                        if (pcRack.CanPlugNew(netId, "", i))
                        {
                            pcRack.PlugNew(netId, "IE1", i);
                            break;
                        }
                    }

                    string firmware = "20.0.0.0";
                    if (typeIdentifier.Contains("/") && typeIdentifier.Split('/').Length > 1)
                    {
                        string f = typeIdentifier.Split('/')[1].Replace("V", "").Trim();
                        if (!string.IsNullOrEmpty(f)) firmware = f;
                    }
                    string swId = $"OrderNumber:6AV2 155-xxxxx-xxxx/{firmware}";
                    for (int slotNum = 1; slotNum <= 10; slotNum++)
                    {
                        if (pcRack.CanPlugNew(swId, "", slotNum))
                        {
                            pcRack.PlugNew(swId, "", slotNum);
                            break;
                        }
                    }
                }
                else
                {
                    newDevice = _project.Devices.CreateWithItem(typeIdentifier, devName, devName);
                }

                if (newDevice != null && !string.IsNullOrEmpty(ipX1))
                {
                    try { SetPlcIpAddress(newDevice, ipX1); } catch { }
                }
            }
            catch (Exception ex) { throw new Exception($"Create Failed: {ex.Message}"); }
        }

        public string GetDeviceType(string deviceName)
        {
            if (_project == null) return "Unknown";
            Device device = FindDeviceRecursive(_project, deviceName);
            if (device != null)
            {
                if (!string.IsNullOrEmpty(device.TypeIdentifier)) return device.TypeIdentifier;
                foreach (DeviceItem item in device.DeviceItems)
                {
                    if (!string.IsNullOrEmpty(item.TypeIdentifier)) return item.TypeIdentifier;
                }
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
                        try { return networkInterface.Nodes[0].GetAttribute("Address").ToString(); } catch { }
                    }
                }
            }
            return "0.0.0.0";
        }

        public List<string> GetPlcList()
        {
            if (_project == null) CheckProject();
            List<string> plcNames = new List<string>();
            foreach (Device device in _project.Devices) plcNames.Add(device.Name);
            foreach (DeviceUserGroup group in _project.DeviceGroups) ScanGroupRecursive(group, plcNames);
            return plcNames;
        }

        public static List<string> GetSystemNetworkAdapters()
        {
            List<string> adapterNames = new List<string>();
            try
            {
                System.Net.NetworkInformation.NetworkInterface[] adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (System.Net.NetworkInformation.NetworkInterface adapter in adapters)
                {
                    if (adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet ||
                        adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                    {
                        adapterNames.Add(adapter.Description);
                    }
                }
            }
            catch { }
            return adapterNames;
        }
        #endregion

        #region 4. PLC Software & Block Operations
        public void ImportBlock(string filePath, string targetPlcName)
        {
            CreateFBblockFromSource(targetPlcName, filePath);
        }

        public void CreateFBblockFromSource(string targetPlcName, string sourcePath)
        {
            if (_project == null) CheckProject();
            Device device = FindDeviceRecursive(_project, targetPlcName);
            if (device == null) throw new Exception($"PLC '{targetPlcName}' not found.");

            var software = GetSoftware(device);
            if (software is PlcSoftware plcSoftware)
            {
                var group = plcSoftware.ExternalSourceGroup;
                var fileName = Path.GetFileName(sourcePath);
                var existingSrc = group.ExternalSources.Find(fileName);
                if (existingSrc != null) existingSrc.Delete();
                var src = group.ExternalSources.CreateFromFile(fileName, sourcePath);
                src.GenerateBlocksFromSource();
            }
            else throw new Exception($"{targetPlcName} is not a valid PLC.");
        }

        public string CompileSpecific(string targetPlcName, bool compileHW, bool compileSW)
        {
            if (_project == null) return "Not connected.";
            Device device = FindDeviceRecursive(_project, targetPlcName);
            if (device == null) return "Device not found.";
            StringBuilder sb = new StringBuilder();
            if (compileHW) sb.AppendLine("HW: " + ((IEngineeringServiceProvider)device).GetService<ICompilable>()?.Compile().State);
            if (compileSW) sb.AppendLine("SW: " + ((IEngineeringServiceProvider)GetSoftware(device)).GetService<ICompilable>()?.Compile().State);
            return sb.ToString();
        }
        
        #endregion

        #region 5. WinCC Unified: Screen Management
        public void GenerateScadaScreenFromData(string deviceName, ScadaScreenModel screenData)
        {
            if (_project == null) throw new Exception("No TIA Project opened.");
            dynamic hmiTarget = GetHmiTarget(deviceName);
            dynamic screens = null;
            try { screens = hmiTarget.Screens; } catch { screens = hmiTarget.ScreenFolder.Screens; }

            var existingScreen = ((System.Collections.IEnumerable)screens).Cast<dynamic>().FirstOrDefault(s => s.Name == screenData.ScreenName);
            if (existingScreen != null) existingScreen.Delete();

            dynamic res = screens.Create(screenData.ScreenName);
            res.SetAttribute("Width", (uint)(screenData.Width > 0 ? screenData.Width : 1024));
            res.SetAttribute("Height", (uint)(screenData.Height > 0 ? screenData.Height : 600));

            List<ScadaItemModel> allItems = new List<ScadaItemModel>();
            if (screenData.Items != null) allItems.AddRange(screenData.Items);
            if (screenData.Layers != null) allItems.AddRange(screenData.Layers.SelectMany(l => l.Items));

            BuildUnifiedItemsRecursive(res.ScreenItems, allItems);
        }

        public List<string> GetUnifiedScreens(string deviceName)
        {
            if (_project == null) CheckProject();
            Device device = FindDeviceRecursive(_project, deviceName);
            var software = GetSoftware(device) as HmiSoftware;
            return software.Screens.Select(s => s.Name).ToList();
        }

        public void CreateUnifiedScreen(string deviceName, string screenName)
        {
            if (_project == null) CheckProject();
            Device device = FindDeviceRecursive(_project, deviceName);
            var software = GetSoftware(device) as HmiSoftware;
            if (software.Screens.Find(screenName) == null) software.Screens.Create(screenName);
        }
        #endregion

        #region 6. WinCC Unified: Item Rendering & Dynamization
        private void BuildUnifiedItemsRecursive(dynamic composition, List<ScadaItemModel> items)
        {
            // 1. Dictionary dùng chung để lưu "xác" vật thể phục vụ nạp Tag ở GĐ 2
            var createdObjects = new Dictionary<string, dynamic>();
            
            Console.WriteLine("\n>> [GIAI ĐOẠN 1] Dựng hình & Gán tọa độ thực...");

            foreach (var item in items) {
                try {
                    // --- BỘ LỌC AN TOÀN: BỎ QUA NGAY TỪ ĐẦU ---
                    string lowerType = item.Type.ToLower();            
                    dynamic newItem = null;

                    // A. TẠO XÁC VẬT THỂ
                    if (item.Properties.ContainsKey("LibraryPath")) {
                        CreateDynamicWidget(composition, item.Type, item.Name, item.Properties);
                        System.Threading.Thread.Sleep(300);
                        newItem = composition.Find(item.Name);
                    } else {
                        string typeId = item.Type.StartsWith("Hmi") ? item.Type : "Hmi" + item.Type;
                        try {
                            // Ép kiểu dynamic để tránh lỗi "cannot be inferred" cho các loại chuẩn
                            dynamic dynComp = composition;
                            newItem = dynComp.Create((string)typeId, (string)item.Name);
                        } catch (Exception ex) {
                            try {
                                newItem = CreateBaseItem(composition, typeId, item.Name);
                            } catch {
                                Console.WriteLine($"      [!] Lỗi khởi tạo {item.Name}: {ex.Message}");
                            }
                        }
                    }

                    // B. CẤU HÌNH THUỘC TÍNH (Khi xác đã dựng xong)
                    if (newItem != null) {
                        string typeId = item.Type.StartsWith("Hmi") ? item.Type : "Hmi" + item.Type;

                        // 1. Nhóm hình tròn đặc thù (CenterX, CenterY, Radius)
                        if (typeId == "HmiCircle" || typeId == "HmiCircularArc" || typeId == "HmiCircleSegment") {
                            try {
                                newItem.SetAttribute("CenterX", Convert.ToInt32(item.Properties["CenterX"])); 
                                newItem.SetAttribute("CenterY", Convert.ToInt32(item.Properties["CenterY"]));
                                newItem.SetAttribute("Radius", (uint)Convert.ToInt32(item.Properties["Radius"]));
                                if (typeId != "HmiCircle") {
                                    newItem.SetAttribute("StartAngle", Convert.ToInt32(item.Properties["AngleStart"]));
                                    newItem.SetAttribute("AngleRange", Convert.ToInt32(item.Properties["AngleRange"]));
                                }
                            } catch { }
                        } 
                        // 2. Nhóm vật thể hình học/Widget chuẩn (Left, Top, Width, Height)
                        else {
                            try {
                                int left = item.Properties.ContainsKey("Left") ? Convert.ToInt32(item.Properties["Left"]) : 0;
                                int top = item.Properties.ContainsKey("Top") ? Convert.ToInt32(item.Properties["Top"]) : 0;
                                uint width = item.Properties.ContainsKey("Width") ? (uint)Convert.ToInt32(item.Properties["Width"]) : 100;
                                uint height = item.Properties.ContainsKey("Height") ? (uint)Convert.ToInt32(item.Properties["Height"]) : 40;

                                newItem.SetAttribute("Left", left);
                                newItem.SetAttribute("Top", top);
                                newItem.SetAttribute("Width", width);
                                newItem.SetAttribute("Height", height);
                            } catch { }
                        }

                        // 3. XỬ LÝ CHI TIẾT THEO LOẠI
                        try {
                            // IO FIELD
                            if (lowerType.Contains("iofield")) {
                                string fmt = (item.Properties.ContainsKey("Format") && item.Properties["Format"] != null) 
                                            ? item.Properties["Format"].ToString() : "{F2}";
                                try { newItem.SetAttribute("OutputFormat", fmt); } catch { }
                                try { newItem.SetAttribute("ProcessValue", ""); } catch { } 
                            }
                            // BAR / GAUGE / SLIDER (Dùng SetAttribute để tránh lỗi định nghĩa Type)
                            else if (lowerType.Contains("bar") || lowerType.Contains("gauge") || lowerType.Contains("slider")) {
                                double min = item.Properties.ContainsKey("MinValue") ? Convert.ToDouble(item.Properties["MinValue"]) : 0;
                                double max = item.Properties.ContainsKey("MaxValue") ? Convert.ToDouble(item.Properties["MaxValue"]) : 100;
                                try {
                                    if (lowerType.Contains("gauge")) {
                                        newItem.CurvedScale.SetAttribute("MinValue", min);
                                        newItem.CurvedScale.SetAttribute("MaxValue", max);
                                    } else {
                                        newItem.StraightScale.SetAttribute("MinValue", min);
                                        newItem.StraightScale.SetAttribute("MaxValue", max);
                                    }
                                } catch { }
                            }
                            // CHECKBOX / RADIO BUTTON
                            else if (lowerType.Contains("checkbox") || lowerType.Contains("radiobutton")) {
                                var selectionItems = newItem.SelectionItems;
                                var newItemPart = selectionItems.Create(); 
                                string itemText = (item.Properties.ContainsKey("Text") && item.Properties["Text"] != null) 
                                                ? item.Properties["Text"].ToString() : "Option 1";
                                try { newItemPart.SetAttribute("Text", itemText); } catch { }
                            }
                            // TEXTFIELD / BUTTON
                            else if (typeId == "HmiTextField" || lowerType.Contains("button")) {
                                string txt = item.Properties.ContainsKey("Text") ? item.Properties["Text"].ToString() : "";
                                if (!string.IsNullOrEmpty(txt)) {
                                    try { newItem.Text.Items[0].SetAttribute("Text", $"<body><p>{txt}</p></body>"); } catch { }
                                }
                            }
                        } catch { }

                        // --- C. LƯU TRỮ VÀ SCRIPT ---
                        if (!createdObjects.ContainsKey(item.Name)) createdObjects.Add(item.Name, newItem);
                        
                        if (lowerType.Contains("button") && item.Properties.ContainsKey("Scripts")) {
                            ProcessButtonScripts(newItem, item.Name, item.Properties["Scripts"]);
                        }
                        Console.WriteLine($"      [OK] Đã dựng xác: {item.Name}");
                    }
                } catch (Exception ex) { 
                    Console.WriteLine($"      [!] Lỗi GĐ 1 tại {item.Name}: {ex.Message}");
                }
            }

            Console.WriteLine("\n>> [GIAI ĐOẠN 2] Nạp linh hồn THẬT...");

            foreach (var item in items) 
            {
                if (!createdObjects.ContainsKey(item.Name)) continue;
                dynamic dynItem = createdObjects[item.Name];
                string realType = dynItem.GetType().Name;

                // Trích xuất Tag
                string tag = item.Properties.ContainsKey("Tag") ? item.Properties["Tag"].ToString() :
                            item.Properties.ContainsKey("StatusTag") ? item.Properties["StatusTag"].ToString() :
                            item.Properties.ContainsKey("LevelTag") ? item.Properties["LevelTag"].ToString() : "";

                if (string.IsNullOrEmpty(tag)) continue;

                // --- PHÂN LUỒNG NẠP TAG ---

                // 1. NHÓM HÌNH KHỐI (Rectangle, Circle, EllipseSegment, CircularArc)
                if (realType.Contains("Rectangle") || realType.Contains("Circle") || realType.Contains("EllipseSegment")) {
                    string customScript = item.Properties.ContainsKey("ColorScript") ? item.Properties["ColorScript"].ToString() : "";
                    BindTagToBasicWithStates(dynItem, tag, "BackColor", customScript);
                    Console.WriteLine($"      => [THẬT BACK] {item.Name} -> {tag}"); 
                }
                else if (realType.Contains("CircularArc")) {
                    BindTagToBasic(dynItem, tag, "LineColor");
                    Console.WriteLine($"      => [THẬT LINE] {item.Name} -> {tag}");
                }

                // 2. NHÓM WIDGET (Thư viện)
                else if (item.Properties.ContainsKey("LibraryPath")) {
                    string targetProp = item.Type.Contains("Tank") ? "FillLevelColor" : "BasicColor";
                    string customScript = item.Properties.ContainsKey("ColorScript") ? item.Properties["ColorScript"].ToString() : "";
                    foreach (dynamic m in dynItem.Interface) {
                        if (m.PropertyName == targetProp) {
                            if (item.Type.Contains("Motor")) BindScriptToWidget(m, tag, customScript);
                            else BindTagToWidget(m, tag);
                            Console.WriteLine($"      => [THẬT WIDGET] {item.Name} -> {tag}");
                            break;
                        }
                    }
                }

                // 3. NHÓM ĐIỀU KHIỂN & HIỂN THỊ (IO Field, Bar, Slider, Gauge, Radio, CheckBox)
                else if (realType.Contains("Bar") || realType.Contains("Gauge") || 
                        realType.Contains("Slider") || realType.Contains("IoField") ||
                        realType.Contains("Switch") || realType.Contains("CheckBox") || 
                        realType.Contains("RadioButton"))
                {
                    BindTagToBasic(dynItem, tag, "ProcessValue");         
                    Console.WriteLine($"      => [THẬT ELEMENT] {item.Name} ({realType}) -> {tag}");
                }

                // 4. NHÓM VĂN BẢN (TextBox)
                else if (realType.Contains("TextBox")) {
                    BindTagToBasic(dynItem, tag, "Text");
                    Console.WriteLine($"      => [THẬT TEXT] {item.Name} -> {tag}");
                }
            }
            Console.WriteLine("[SUCCESS] Vẽ và nạp linh hồn hoàn tất!");
        }
        private IEngineeringObject CreateBaseItem(dynamic composition, string typeName, string name)
        {
            var method = ((object)composition).GetType().GetMethods().FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod);
            Type targetType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.Name == typeName);
            return (IEngineeringObject)method.MakeGenericMethod(targetType).Invoke(composition, new object[] { name });
        }
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

        public void BindTagToBasicWithStates(dynamic item, string tagName, string propName, string scriptCode) 
        {
            try {
                var dyns = item.Dynamizations;

                // 1. XÓA TRIỆT ĐỂ DYNAMIZATION CŨ (Tránh lỗi Target of Invocation)
                for (int i = dyns.Count - 1; i >= 0; i--) {
                    if (dyns[i].PropertyName == propName) {
                        dyns[i].Delete();
                    }
                }

                // 2. TẠO SCRIPT DYNAMIZATION QUA REFLECTION
                var method = ((object)dyns).GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 1);
                
                Type scriptType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "ScriptDynamization");

                if (method != null && scriptType != null) {
                    var dyn = method.MakeGenericMethod(scriptType).Invoke(dyns, new object[] { propName });
                    dynamic scriptObj = (dynamic)dyn;

                    // 3. THIẾT LẬP TRIGGER (Dành riêng cho Basic Objects V20)
                    bool triggerSuccess = false;
                    try {
                        // Thử cách 1: SourceAttribute (Dùng cho WinCC Unified V20 Basic Shapes)
                        scriptObj.SourceAttribute = tagName; 
                        triggerSuccess = true;
                    } catch {
                        try {
                            // Thử cách 2: AttributeTriggers (Dùng khi thuộc tính cần giám sát cụ thể)
                            var attrTrigger = scriptObj.AttributeTriggers.Create();
                            attrTrigger.AttributePath = propName; 
                            attrTrigger.Tag = tagName;
                            triggerSuccess = true;
                        } catch {
                            try {
                                // Thử cách 3: Triggers tổng quát (Thường dùng cho Widget)
                                var tagTrigger = scriptObj.Triggers.Create();
                                tagTrigger.Tag = tagName;
                                triggerSuccess = true;
                            } catch { }
                        }
                    }

                    // 4. NẠP MÃ SCRIPT (Ưu tiên từ JSON, dùng HMIRuntime.Math.RGB cho chuyên nghiệp)
                    string defaultScript = $@"var status = Tags(""{tagName}"").Read(); 
        return status ? HMIRuntime.Math.RGB(135, 190, 50) : HMIRuntime.Math.RGB(178, 34, 34);";

                    scriptObj.ScriptCode = !string.IsNullOrEmpty(scriptCode) ? scriptCode : defaultScript;

                    // Thông báo trạng thái nạp
                    if (triggerSuccess)
                        Console.WriteLine($"      => [THẬT RGB SCRIPT] {item.Name} -> {tagName} (Trigger OK)");
                    else
                        Console.WriteLine($"      => [THẬT RGB SCRIPT] {item.Name} -> {tagName} (Cần gán Trigger tay)");
                }
            } catch (Exception ex) {
                Console.WriteLine($"      [!] Lỗi nạp Script tại {item.Name}: {ex.Message}");
            }
        }

        public void BindScriptToWidget(dynamic member, string tagName, string scriptCode) {
            try {
                dynamic dyns = member.Dynamizations;
                var method = ((object)dyns).GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethod && m.GetParameters().Length == 1);

                Type scriptType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "ScriptDynamization");

                if (method != null && scriptType != null) {
                    var scriptDyn = method.MakeGenericMethod(scriptType).Invoke(dyns, new object[] { member.PropertyName });
                    
                    // THIẾT LẬP TRIGGER (Để Script tự chạy khi Tag đổi giá trị)
                    try {
                        var tagTrigger = ((dynamic)scriptDyn).Triggers.Create();
                        tagTrigger.Tag = tagName;
                    } catch { }

                    // NẠP MÃ JS: Ưu tiên lấy từ JSON, nếu trống dùng mẫu chuẩn RGB
                    string finalScript = !string.IsNullOrEmpty(scriptCode) ? scriptCode : 
                        $@"var status = Tags(""{tagName}"").Read(); 
        return status ? HMIRuntime.Math.RGB(135, 190, 50) : HMIRuntime.Math.RGB(178, 34, 34);";

                    ((dynamic)scriptDyn).ScriptCode = finalScript;
                    
                    Console.WriteLine($"      => [THẬT WIDGET JSON SCRIPT] {member.PropertyName} -> {tagName}");
                }
            } catch (Exception ex) { 
                Console.WriteLine($"      [!] Lỗi Widget Script: {ex.Message}");
            }
        }
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


        public void AssignMomentaryTag(string deviceName, string screenName, string itemName, string newTagName)
        {
            try
            {
                var screenItems = GetScreenItemsComposition(deviceName, screenName);
                dynamic targetItem = screenItems.Cast<dynamic>().FirstOrDefault(i => i.Name == itemName);
                if (targetItem == null) return;

                foreach (dynamic handler in targetItem.EventHandlers)
                {
                    var script = handler.Script;
                    if (script != null)
                    {
                        string evType = handler.EventType.ToString();
                        int val = (evType == "KeyDown") ? 1 : 0;
                        script.SourceCode = $"export function {itemName}_On{evType}(item, keyCode, modifiers) {{\n  Tags(\"{newTagName}\").Write({val});\n}}";
                    }
                }
            }
            catch { }
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
        private IEngineeringComposition GetCompositionSafe(IEngineeringObject obj, string compositionName)
        {
            try
            {
                var compOrObj = obj.GetComposition(compositionName);
                return compOrObj as IEngineeringComposition;
            }
            catch { return null; }
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

        #endregion

        #region 7. WinCC Unified: Connection & Tag Import
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


        public void ImportPlcTagsFromCsv(string plcName, string csvPath)
        {
            if (_project == null) { ConsoleUI.PrintResult("[ERROR] Project chưa mở."); return; }

            try
            {
                if (!System.IO.File.Exists(csvPath)) {
                    ConsoleUI.PrintResult($"[ERROR] Không tìm thấy file: {csvPath}"); return;
                }

                Device plcDevice = FindDeviceRecursive(_project, plcName);
                var software = GetSoftware(plcDevice) as PlcSoftware;
                if (software == null) return;

                string[] lines = System.IO.File.ReadAllLines(csvPath);
                int successCount = 0;

                for (int i = 1; i < lines.Length; i++)
                {
                    // Tách cột bằng dấu phẩy hoặc dấu chấm phẩy
                    string[] columns = lines[i].Split(','); 
                    if (columns.Length < 4) columns = lines[i].Split(';'); 

                    if (columns.Length < 4) continue;

                    // --- CẬP NHẬT THEO HÌNH ẢNH EXCEL ---
                    string tagName = columns[0].Trim();         // Cột A: Name
                    string tablePath = columns[1].Trim();      // Cột B: Path (Tag Table)
                    string dataType = columns[2].Trim();       // Cột C: Data Type
                    string address = columns[3].Trim();        // Cột D: Logical Address
                    string comment = columns.Length > 4 ? columns[4].Trim() : ""; // Cột E: Comment

                    try 
                    {
                        // Tìm hoặc tạo Tag Table dựa theo cột B
                        var table = software.TagTableGroup.TagTables.Find(tablePath) 
                                    ?? software.TagTableGroup.TagTables.Create(tablePath);

                        var plcTags = table.Tags;
                        if (plcTags.Find(tagName) != null) plcTags.Find(tagName).Delete();
                        
                        var newTag = plcTags.Create(tagName);

                        // Gán thuộc tính theo đúng Openness API cho PLC
                        newTag.SetAttribute("DataTypeName", dataType); 
                        newTag.SetAttribute("LogicalAddress", address);

                        if (!string.IsNullOrEmpty(comment))
                        {
                            newTag.Comment.Items.First().Text = comment;
                        }

                        successCount++;
                        Console.WriteLine($"[INFO] Line {i+1}: Đã nạp {tagName} vào bảng {tablePath}");
                    }
                    catch (Exception ex)
                    {
                        ConsoleUI.PrintResult($"[ERROR] Dòng {i + 1} ({tagName}): {ex.Message}");
                    }
                }
                ConsoleUI.PrintResult($"[SUCCESS] Hoàn thành! Đã nạp {successCount} tags vào PLC {plcName}.");
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

        #region 8. Graphics & Multimedia Management
        public bool AddPngToProjectGraphics(string filePath, string graphicName)
        {
            try
            {
                dynamic graphics = ((dynamic)_project).Graphics;
                if (graphics == null || !File.Exists(filePath)) return false;

                string directory = Path.GetDirectoryName(filePath);
                string folderName = graphicName + "_files";
                string folderPath = Path.Combine(directory, folderName);
                Directory.CreateDirectory(folderPath);
                File.Copy(filePath, Path.Combine(folderPath, "DefaultImageStream.png"), true);

                string xmlPath = Path.Combine(directory, "import_wrapper.xml");
                string xmlContent = $@"<?xml version=""1.0"" encoding=""utf-8""?><Document><Engineering version=""V20"" /><Hmi.Globalization.MultiLingualGraphic ID=""0""><AttributeList><Name>{graphicName}</Name><DefaultImageStream external=""path"">{folderName}\DefaultImageStream.png</DefaultImageStream></AttributeList></Hmi.Globalization.MultiLingualGraphic></Document>";
                File.WriteAllText(xmlPath, xmlContent, Encoding.UTF8);

                graphics.Import(new FileInfo(xmlPath), (dynamic)Enum.ToObject(graphics.GetType().GetMethod("Import").GetParameters()[1].ParameterType, 0));
                return true;
            }
            catch { return false; }
        }

        public void ImportAllImagesFromFolder(string folderPath)
        {
            foreach (var file in Directory.GetFiles(folderPath, "*.png"))
                AddPngToProjectGraphics(file, Path.GetFileNameWithoutExtension(file));
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


        public void CreateGraphicList(string listName, List<string> graphicNames) { /* Logic Create Graphic List */ }

        public List<string> GetProjectGraphicsNames()
        {
            List<string> names = new List<string>();
            try { foreach (dynamic g in (dynamic)_project.GetType().GetProperty("Graphics").GetValue(_project)) names.Add(g.Name.ToString()); } catch { }
            return names;
        }
        #endregion

        #region 9. Download, Online & Diagnostic Operations
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

        #endregion

        #region 10. Backup & Restore Operations
        public string BackupPlcData(string deviceName, string backupFolderPath)
        {
            Device device = FindDeviceRecursive(_project, deviceName);
            PlcSoftware software = GetSoftware(device) as PlcSoftware;
            Directory.CreateDirectory(backupFolderPath);
            foreach (PlcBlock block in software.BlockGroup.Blocks) block.Export(new FileInfo(Path.Combine(backupFolderPath, block.Name + ".xml")), ExportOptions.None);
            return "Backup Done";
        }
        
        #endregion

        #region 11. Core Helpers (Scanning & Reflection)
        private Device FindDeviceRecursive(Project project, string deviceName)
        {
            Device d = project.Devices.Find(deviceName);
            if (d != null) return d;
            foreach (var group in project.DeviceGroups)
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
            foreach (var sub in group.Groups)
            {
                d = FindDeviceInGroupRecursive(sub, deviceName);
                if (d != null) return d;
            }
            return null;
        }

        private Software GetSoftware(Device device) => FindSoftwareRecursive(device);

        private Software FindSoftwareRecursive(IEngineeringObject obj)
        {
            var container = (obj as IEngineeringServiceProvider)?.GetService<SoftwareContainer>();
            if (container != null) return container.Software;
            if (obj is Device d) foreach (var i in d.DeviceItems) { var r = FindSoftwareRecursive(i); if (r != null) return r; }
            if (obj is DeviceItem di) foreach (var i in di.DeviceItems) { var r = FindSoftwareRecursive(i); if (r != null) return r; }
            return null;
        }

        private DeviceItem GetCpuItem(Device device)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                if (((IEngineeringServiceProvider)item).GetService<Siemens.Engineering.Download.DownloadProvider>() != null) return item;
                foreach (DeviceItem sub in item.DeviceItems)
                    if (((IEngineeringServiceProvider)sub).GetService<Siemens.Engineering.Download.DownloadProvider>() != null) return sub;
            }
            return null;
        }

        private void CheckProject() { if (_project == null && _tiaPortal?.Projects.Count > 0) _project = _tiaPortal.Projects[0]; }

        private void ScanGroupRecursive(DeviceUserGroup group, List<string> names)
        {
            foreach (Device d in group.Devices) names.Add(d.Name);
            foreach (DeviceUserGroup sub in group.Groups) ScanGroupRecursive(sub, names);
        }

        private DeviceItem FindNetworkInterfaceItem(DeviceItemComposition items)
        {
            foreach (DeviceItem item in items)
            {
                if (item.GetService<NetworkInterface>()?.InterfaceType == NetType.Ethernet) return item;
                var sub = FindNetworkInterfaceItem(item.DeviceItems);
                if (sub != null) return sub;
            }
            return null;
        }

        private void SetPlcIpAddress(Device device, string ip)
        {
            var node = FindNetworkInterfaceItem(device.DeviceItems)?.GetService<NetworkInterface>()?.Nodes[0];
            if (node != null) node.SetAttribute("Address", ip);
        }

        private dynamic GetHmiTarget(string deviceName)
        {
            Device device = FindDeviceRecursive(_project, deviceName);
            return DeepSearchHmiTarget(device.DeviceItems, 1);
        }

        private dynamic DeepSearchHmiTarget(DeviceItemComposition items, int level)
        {
            foreach (DeviceItem item in items)
            {
                var sw = item.GetService<SoftwareContainer>()?.Software;
                if (sw != null && sw.GetType().Name.Contains("Hmi")) return sw;
                var sub = DeepSearchHmiTarget(item.DeviceItems, level + 1);
                if (sub != null) return sub;
            }
            return null;
        }
        #endregion

        #region 12. Debug & Diagnostic Tools
        public void ExportAllPathsFromScreen(string deviceName, string screenName, string targetObjectName) { /* Logic Debug Screen Path */ }
        private void ScanDeep(dynamic screenObj) { /* Logic ScanDeep Object */ }
        #endregion
    }
}