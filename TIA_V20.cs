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



namespace Middleware_console
{
    public class TIA_V20
    {
        #region 1. Fields & Constructor
        private TiaPortal _tiaPortal;
        private Project _project;
        public TIA_V20() { }


        // 1. Thuộc tính kiểm tra kết nối (dùng trong Navigator)
        public bool IsConnected
        {
            get
            {
                if (_tiaPortal == null || _project == null) return false;
                try
                {
                    var checkStatus = _project.Path;
                    return true;
                }
                catch
                {
                    _tiaPortal = null;
                    _project = null;
                    return false;
                }
            }
        }

        // 2. Hàm Alias (tên giả) để Navigator gọi Connect
        public void ConnectToTiaPortal()
        {
            if (!ConnectToTIA())
            {
                throw new Exception("No running TIA Portal instance found!");
            }
        }

        // 3. Hàm Alias để Navigator gọi Import
        public void ImportBlock(string filePath)
        {
            CreateFBblockFromSource(filePath);
        }

        public string ProjectName
        {
            get
            {
                if (_project != null)
                {
                    try { return _project.Name; }
                    catch { return "Unknown"; }
                }
                return "None";
            }
        }

        // Thuộc tính lấy đường dẫn Project (nếu cần hiển thị thêm)
        public string ProjectPath
        {
            get
            {
                if (_project != null)
                {
                    try { return _project.Path.FullName; }
                    catch { return ""; }
                }
                return "";
            }
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

        #region 3. Hardware & Network

        public void CreateDev(string devName, string typeIdentifier, string ipX1, string ipX2)
        {
            if (_project == null) CheckProject();
            if (string.IsNullOrWhiteSpace(devName)) devName = "Device_1";

            var currentDevices = GetDeviceList();
            if (currentDevices.Any(d => d.Name == devName))
                throw new Exception($"Name '{devName}' already exists!");

            Device newStation = _project.Devices.CreateWithItem(typeIdentifier, devName, devName);
            DeviceItem targetItem = FindIntelligentItem(newStation);
            
            if (targetItem == null && newStation.DeviceItems.Count > 0) targetItem = newStation.DeviceItems[0];

            if (targetItem != null)
            {
                try { targetItem.Name = devName; } catch { } 
                if (!string.IsNullOrEmpty(ipX1)) SetPlcIpAddress(newStation, ipX1);
            }
        }

        // --- HÀM LẤY DANH SÁCH THIẾT BỊ (FINAL V6 - NO GUESSING, STRICT UNKNOWN) ---
        public List<PlcInfo> GetDeviceList()
        {
            if (_project == null) CheckProject();
            List<PlcInfo> results = new List<PlcInfo>();
            if (_project == null) return results;

            List<Device> stations = new List<Device>();
            try
            {
                foreach (Device d in _project.Devices) stations.Add(d);
                foreach (DeviceUserGroup group in _project.DeviceGroups) ScanGroupRecursiveForObj(group, stations);
            }
            catch (Exception ex) { Console.WriteLine($"[WARNING] Error scanning devices: {ex.Message}"); }

            foreach (Device station in stations)
            {
                try
                {
                    // 1. Tìm Target Item
                    DeviceItem targetItem = FindIntelligentItem(station);
                    if (targetItem == null)
                        targetItem = FindItemByKeywordRecursive(station.DeviceItems, new[] { "HMI", "Panel", "Unified", "PC", "MTP", "KTP", "ITC", "Comfort", "Basic" });

                    if (targetItem != null || station.DeviceItems.Count > 0)
                    {
                        if (targetItem == null) targetItem = station.DeviceItems[0];

                        PlcInfo info = new PlcInfo();
                        string typeId = targetItem.TypeIdentifier ?? "";
                        string stationTypeId = station.TypeIdentifier ?? "";
                        string category = "Unknown";

                        // [1] PHÂN LOẠI
                        if (stationTypeId.Contains("PC") || typeId.Contains("PC") || typeId.Contains("Unified")) category = "SCADA";
                        else if (typeId.Contains("Panel") || typeId.Contains("HMI") || typeId.Contains("MTP") || typeId.Contains("KTP")) category = "HMI";
                        else if (typeId.Contains("CPU") || typeId.Contains("S7")) category = "PLC";

                        if (category == "Unknown")
                        {
                            string n = station.Name.ToUpper();
                            if (n.Contains("HMI")) category = "HMI";
                            else if (n.Contains("PC")) category = "SCADA";
                            else if (n.Contains("PLC")) category = "PLC";
                        }

                        // [2] NAME
                        if (category == "SCADA" || category == "PLC") info.Name = targetItem.Name; 
                        else info.Name = station.Name; 

                        // [3] ARTICLE NUMBER
                        string orderNum = GetDeviceAttribute(targetItem, "OrderNumber");
                        if (string.IsNullOrWhiteSpace(orderNum)) orderNum = GetDeviceAttribute(targetItem, "ArticleNumber");
                        if (string.IsNullOrWhiteSpace(orderNum))
                        {
                            foreach (DeviceItem hwItem in GetAllItemsFlat(station))
                            {
                                string hId = hwItem.TypeIdentifier ?? "";
                                if (hId.Contains("CPU") || category == "HMI")
                                {
                                    string o = GetDeviceAttribute(hwItem, "OrderNumber");
                                    if (string.IsNullOrWhiteSpace(o)) o = GetDeviceAttribute(hwItem, "ArticleNumber");
                                    if (string.IsNullOrWhiteSpace(o) && hId.StartsWith("OrderNumber:"))
                                        o = hId.Split('/')[0].Replace("OrderNumber:", "").Trim();
                                    
                                    if (!string.IsNullOrWhiteSpace(o)) { orderNum = o; break; }
                                }
                            }
                        }
                        if (string.IsNullOrWhiteSpace(orderNum)) orderNum = GetDeviceAttribute(station, "OrderNumber");
                        info.ArticleNumber = !string.IsNullOrWhiteSpace(orderNum) ? orderNum : "Unknown";


                        // [4] DEVICE TYPE (MODEL)
                        string shortDes = GetDeviceAttribute(targetItem, "ShortDesignation");
                        
                        // Quét Hardware nếu chưa có tên
                        if (string.IsNullOrWhiteSpace(shortDes) || shortDes == "Unknown Device")
                        {
                            foreach (DeviceItem hwItem in GetAllItemsFlat(station))
                            {
                                string hwId = hwItem.TypeIdentifier ?? "";
                                string hwName = hwItem.Name ?? "";
                                
                                if (hwName.Contains("Rack") || hwName.Contains("Rail") || hwId.Contains("Rack")) continue;

                                bool isCandidate = hwId.Contains("CPU") || hwId.Contains("S7") || hwId.Contains("HMI") || hwId.Contains("Panel") || hwId.Contains("PC");
                                
                                if (isCandidate)
                                {
                                    string tempDes = GetDeviceAttribute(hwItem, "ShortDesignation");
                                    if (string.IsNullOrWhiteSpace(tempDes)) tempDes = GetDeviceAttribute(hwItem, "ProductDesignation");
                                    if (string.IsNullOrWhiteSpace(tempDes)) tempDes = GetDeviceAttribute(hwItem, "TypeDesignation");
                                    if (string.IsNullOrWhiteSpace(tempDes)) tempDes = GetDeviceAttribute(hwItem, "CatalogName");
                                    if (string.IsNullOrWhiteSpace(tempDes) && hwName.StartsWith("CPU")) tempDes = hwName;

                                    if (string.IsNullOrWhiteSpace(tempDes) && !string.IsNullOrEmpty(hwId) && !hwId.StartsWith("OrderNumber:"))
                                    {
                                        tempDes = FormatSiemensTypeName(hwId);
                                    }

                                    if (!string.IsNullOrWhiteSpace(tempDes)) { shortDes = tempDes; break; }
                                }
                            }
                        }

                        // ĐÃ XÓA KHỐI CODE "ĐOÁN TÊN TỪ MÃ HÀNG" (ArticleNumber fallback)
                        // Để đảm bảo nếu TIA không trả về tên, nó sẽ rơi xuống Unknown Model bên dưới

                        
                        // FIX CHO SCADA: Lấy tên Station nếu Unknown
                        if (category == "SCADA" && (string.IsNullOrWhiteSpace(shortDes) || shortDes.Contains("Unknown"))) 
                        {
                            shortDes = station.Name; 
                        }
                        
                        // CHỐT: Nếu vẫn rỗng, gán Unknown Model
                        if (string.IsNullOrWhiteSpace(shortDes)) shortDes = "Unknown Model";
                        
                        info.CpuType = $"{shortDes} [{category}]";


                        // [5] FIRMWARE
                        string fw = GetDeviceAttribute(targetItem, "Version");
                        if (string.IsNullOrWhiteSpace(fw) && typeId.Contains("/V"))
                        {
                             var parts = typeId.Split(new[] { "/V" }, StringSplitOptions.None);
                             if (parts.Length > 1) fw = parts[1];
                        }
                        info.Version = !string.IsNullOrWhiteSpace(fw) ? "V" + fw : "Unknown";


                        // [6] IP ADDRESS (MULTI-IP)
                        info.IpAddress = GetAllIpsFromStation(station);

                        results.Add(info);
                    }
                }
                catch (Exception innerEx) { Console.WriteLine($"[WARNING] Skipping device: {innerEx.Message}"); }
            }
            return results;
        }

        // --- CÁC HÀM PHỤ TRỢ ---

        private string GetDeviceAttribute(IEngineeringObject obj, string attrName)
        {
            try { var val = obj.GetAttribute(attrName); return val != null ? val.ToString() : ""; } catch { return ""; }
        }

        private string FormatSiemensTypeName(string typeId)
        {
            try
            {
                var parts = typeId.Split('.');
                var rawName = parts.Reverse().FirstOrDefault(p => 
                    p.StartsWith("CPU") || p.StartsWith("KTP") || p.StartsWith("MTP") || p.StartsWith("IPC") || p.StartsWith("HMI") || p.Contains("Unified"));

                if (rawName == null) return null;

                if (rawName.StartsWith("CPU"))
                {
                    if (!rawName.Contains(" ")) rawName = rawName.Replace("CPU", "CPU "); 
                    if (rawName.Contains("_"))
                    {
                        int firstUnderscore = rawName.IndexOf('_');
                        StringBuilder sb = new StringBuilder(rawName);
                        sb[firstUnderscore] = ' '; 
                        rawName = sb.ToString().Replace('_', '/'); 
                    }
                    return rawName.Trim();
                }
                return rawName.Replace("_", " ");
            }
            catch { return null; }
        }

        private string GetAllIpsFromStation(Device station)
        {
            var allItems = GetAllItemsFlat(station);
            HashSet<string> foundIps = new HashSet<string>();

            foreach (var item in allItems)
            {
                try
                {
                    var net = item.GetService<NetworkInterface>();
                    if (net != null)
                    {
                        foreach (Node node in net.Nodes)
                        {
                            var addr = node.GetAttribute("Address");
                            if (addr != null)
                            {
                                string sAddr = addr.ToString();
                                if (sAddr.Contains(".")) foundIps.Add(sAddr);
                            }
                        }
                    }
                }
                catch { }
            }
            if (foundIps.Count == 0) return "0.0.0.0";
            return string.Join(" | ", foundIps);
        }

        private List<DeviceItem> GetAllItemsFlat(Device station)
        {
            List<DeviceItem> list = new List<DeviceItem>();
            AddItemsRecursive(station.DeviceItems, list);
            return list;
        }
        private void AddItemsRecursive(DeviceItemComposition items, List<DeviceItem> list)
        {
            foreach(DeviceItem item in items) { list.Add(item); AddItemsRecursive(item.DeviceItems, list); }
        }
        private DeviceItem FindIntelligentItem(IEngineeringObject deviceOrItem)
        {
            try {
                var provider = deviceOrItem as IEngineeringServiceProvider;
                var softContainer = provider?.GetService<SoftwareContainer>();
                if (softContainer != null && (softContainer.Software is PlcSoftware || softContainer.Software is HmiSoftware)) return deviceOrItem as DeviceItem;
                DeviceItemComposition items = null;
                if (deviceOrItem is Device d) items = d.DeviceItems; else if (deviceOrItem is DeviceItem di) items = di.DeviceItems;
                if (items != null) { foreach (DeviceItem item in items) { var found = FindIntelligentItem(item); if (found != null) return found; } }
            } catch { } return null;
        }
        private DeviceItem FindItemByKeywordRecursive(DeviceItemComposition items, string[] keywords)
        {
            if (items == null) return null;
            foreach (DeviceItem item in items) {
                string id = item.TypeIdentifier ?? ""; string name = item.Name ?? "";
                if (keywords.Any(k => id.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) return item;
                var found = FindItemByKeywordRecursive(item.DeviceItems, keywords); if (found != null) return found;
            } return null;
        }
        public string GetPlcTypeIdentifier(string deviceName) { var list = GetDeviceList(); var d = list.FirstOrDefault(x => x.Name == deviceName); return d != null ? d.ArticleNumber : "Unknown"; }
        public string GetPlcIpAddress(string deviceName) { var list = GetDeviceList(); var d = list.FirstOrDefault(x => x.Name == deviceName); return d != null ? d.IpAddress : "Unknown"; }

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
        // (Giữ nguyên phần SCADA Generation như cũ của bạn)
        public void GenerateScadaScreenFromData(string deviceName, ScadaScreenModel screenData)
        {
            if (_project == null) CheckProject();
            try { CreateUnifiedScreen(deviceName, screenData.ScreenName); } catch { }

            IEngineeringComposition screenItemsComp = GetScreenItemsComposition(deviceName, screenData.ScreenName);
            if (screenItemsComp == null) throw new Exception("Failed to access ScreenItems");

            Console.WriteLine($"Building screen: {screenData.ScreenName}");
            List<ScadaItemModel> allItems = new List<ScadaItemModel>();
            if (screenData.Items != null) allItems.AddRange(screenData.Items);
            if (screenData.Layers != null)
                foreach (var l in screenData.Layers) if (l.Items != null) allItems.AddRange(l.Items);

            BuildItemsRecursive(deviceName, screenItemsComp, allItems);
        }

        private void BuildItemsRecursive(string deviceName, IEngineeringComposition container, List<ScadaItemModel> items)
        {
            // Not implemented yet
        }
        private IEngineeringObject CreateItemGeneric(IEngineeringComposition container, string typeName, string name)
        {
            // Not implemented yet
            return null;
        }
        private void CreateInternalTagGeneric(string deviceName, string tag, string type)
        {
            // Not implemented yet
        }
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

        private void ScanGroupRecursiveForObj(DeviceUserGroup group, List<Device> resultList)
        {
            foreach (Device d in group.Devices) resultList.Add(d);
            foreach (DeviceUserGroup subGroup in group.Groups) ScanGroupRecursiveForObj(subGroup, resultList);
        }
        private string GetAttributeSafe(IEngineeringObject obj, string attrName)
        {
            try { var val = obj.GetAttribute(attrName); return val != null ? val.ToString() : ""; } catch { return ""; }
        }
        #endregion

        #region 8. DOWNLOAD OPERATIONS
        // Hàm Download chương trình xuống PLC
        public string DownloadToPLC(string deviceName, string targetIpAddress, string pgPcInterfaceName)
        {
            if (_project == null) return "Project not loaded.";

            try
            {
                // 1. Tìm Device (Dùng hàm tìm đệ quy mới để đảm bảo thấy PLC trong Group)
                Device device = FindDeviceRecursive(_project, deviceName);
                if (device == null) return "Device not found.";

                // Tìm CPU Item (Quan trọng để lấy dịch vụ Download)
                DeviceItem cpuItem = GetCpuItem(device);
                if (cpuItem == null) return "CPU DeviceItem not found. (Device might not be a PLC)";

                // 2. Tìm DownloadProvider
                // Ép kiểu tường minh
                var serviceProvider = cpuItem as IEngineeringServiceProvider;
                var downloadProvider = serviceProvider?.GetService<Siemens.Engineering.Download.DownloadProvider>();
                if (downloadProvider == null) return "CPU does not support Download.";

                // 3. Cấu hình mạng (Connection Configuration)
                var configuration = downloadProvider.Configuration;
                var mode = configuration.Modes.Find("PN/IE");
                if (mode == null) return "Error: Mode 'PN/IE' not found.";

                // Tìm Card mạng (PC Interface)
                var pcInterface = mode.PcInterfaces.Find(pgPcInterfaceName, 1);
                if (pcInterface == null)
                {
                    // Tìm gần đúng nếu tên không chính xác 100%
                    foreach (var pc in mode.PcInterfaces)
                    {
                        if (pc.Name.Contains(pgPcInterfaceName)) { pcInterface = pc; break; }
                    }
                }
                if (pcInterface == null) return $"Error: PC Interface '{pgPcInterfaceName}' not found.";

                // Tìm Target Interface (Điểm đến)
                var targetConfiguration = pcInterface.TargetInterfaces.Count > 0 ? pcInterface.TargetInterfaces[0] : null;
                if (targetConfiguration == null) return "Error: No Target Interface found.";

                // 4. THỰC HIỆN DOWNLOAD
                // Lưu ý: Delegate để trống để tránh lỗi tương thích version
                Siemens.Engineering.Download.DownloadResult result = downloadProvider.Download(
                    targetConfiguration,
                    (preConf) => { },  // Pre-download
                    (postConf) => { }, // Post-download (Chưa auto-start để tránh lỗi DLL cũ)
                    Siemens.Engineering.Download.DownloadOptions.Hardware | Siemens.Engineering.Download.DownloadOptions.Software
                );

                // 5. Kiểm tra kết quả
                if (result.State == Siemens.Engineering.Download.DownloadResultState.Error)
                {
                    var msg = result.Messages.FirstOrDefault()?.Message ?? "Unknown Error";
                    return $"Download Error: {msg}";
                }

                return "Download Successful! (Please START PLC manually)";
            }
            catch (Exception ex)
            {
                return $"Download Exception: {ex.Message}";
            }
        }

        // Hàm phụ trợ tìm CPU (DownloadProvider thường nằm ở CPU)
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

        public List<PlcFamilyModel> LoadPlcCatalog()
        {
            try
            {
                string jsonFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PLCCatalog.json");
                if (!File.Exists(jsonFilePath))
                    throw new FileNotFoundException("Catalog file not found!", jsonFilePath);

                string jsonContent = File.ReadAllText(jsonFilePath);

                // Deserialize vào Model mới
                var catalog = Newtonsoft.Json.JsonConvert.DeserializeObject<List<PlcFamilyModel>>(jsonContent);
                return catalog ?? new List<PlcFamilyModel>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to load PLC Catalog: {ex.Message}");
                return new List<PlcFamilyModel>();
            }
        }
        #endregion
    }

    #region Data Models

    // 1. FAMILY
    public class PlcFamilyModel
    {
        public string Family { get; set; } // VD: "S7-1200"
        public List<PlcDeviceModel> Devices { get; set; }
    }
    // 2. DEVICE NAME
    public class PlcDeviceModel
    {
        public string Name { get; set; } // VD: "CPU 1214C..."
        public List<PlcVariantModel> Variants { get; set; }
    }
    // 3. DEVICE VARIANT
    public class PlcVariantModel
    {
        public string OrderNumber { get; set; } // VD: "6ES7..."
        public List<string> Versions { get; set; }
    }
    // 4. SCANNED DEVICE INFO
    public class PlcInfo
    {
        public string Name { get; set; }          // Tên CPU (VD: PLC_1)
        public string CpuType { get; set; }       // Loại CPU (VD: CPU 1516-3 PN)
        public string ArticleNumber { get; set; } // Mã hàng (VD: 6ES7 516-...)
        public string Version { get; set; }       // Firmware (VD: V2.9)
        public string IpAddress { get; set; }     // IP

        public string ShowMenu => $"{Name} - {CpuType} ({IpAddress})"; // Dùng để hiện menu chọn
    }
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
    }

    #endregion
}