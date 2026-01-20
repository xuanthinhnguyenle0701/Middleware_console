using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Middleware_console
{
    public class SatEngine
    {
        // Biến dynamic để chứa đối tượng SAT mà không cần Add Reference cứng
        private dynamic _satTool;
        private bool _isInitialized = false;

        // Constructor rỗng (để không gây lỗi khi vừa bật phần mềm)
        public SatEngine() { }

        // --- HÀM KHỞI TẠO (CHỈ CHẠY KHI CẦN DÙNG) ---
        private bool Initialize()
        {
            if (_isInitialized) return true;

            Console.WriteLine("\n[DEBUG] --- KHỞI TẠO SAT ENGINE ---");

            // 1. Kiểm tra môi trường 64-bit (BẮT BUỘC)
            if (IntPtr.Size != 8)
            {
                ConsoleUI.PrintResult("CRITICAL ERROR: Chương trình đang chạy ở chế độ 32-bit!");
                Console.WriteLine("--> Hãy vào file .csproj thêm dòng: <PlatformTarget>x64</PlatformTarget>");
                return false;
            }

            try
            {
                // 2. Xác định đường dẫn file DLL
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dllName = "SIMATICAutomationToolAPI.dll";
                string wrapperName = "wrapper_native.dll";

                string dllPath = Path.Combine(baseDir, dllName);
                string wrapperPath = Path.Combine(baseDir, wrapperName);

                // Nếu không thấy ở thư mục gốc, thử tìm trong Libs (để load code)
                if (!File.Exists(dllPath))
                {
                    string libPath = Path.Combine(baseDir, "Libs", dllName);
                    if (File.Exists(libPath)) dllPath = libPath;
                }

                Console.WriteLine($"[CHECK] DLL Path: {dllPath}");
                Console.WriteLine($"[CHECK] Wrapper Path: {wrapperPath}");

                // 3. Kiểm tra file wrapper (QUAN TRỌNG NHẤT)
                if (!File.Exists(wrapperPath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[LỖI THIẾU FILE] Không tìm thấy '{wrapperName}' cạnh file .exe!");
                    Console.ResetColor();
                    Console.WriteLine($"--> Vui lòng copy file '{wrapperName}' (10MB) vào thư mục:");
                    Console.WriteLine($"--> {baseDir}\n");
                    return false;
                }

                if (!File.Exists(dllPath))
                {
                    ConsoleUI.PrintResult($"Lỗi: Không tìm thấy file {dllName}");
                    return false;
                }

                // 4. Load DLL và tạo đối tượng
                Console.WriteLine("[LOADING] Đang nạp thư viện SAT...");
                var assembly = Assembly.LoadFrom(dllPath);
                var toolType = assembly.GetTypes().FirstOrDefault(t => t.Name == "AutomationTool");

                if (toolType != null)
                {
                    _satTool = Activator.CreateInstance(toolType);
                    _isInitialized = true;
                    Console.WriteLine("[SUCCESS] SAT Engine đã sẵn sàng!");
                    return true;
                }
                else
                {
                    ConsoleUI.PrintResult("Lỗi: Không tìm thấy class 'AutomationTool' trong DLL.");
                    return false;
                }
            }
            catch (BadImageFormatException)
            {
                ConsoleUI.PrintResult("Lỗi sai định dạng: Có thể do file DLL bị hỏng hoặc sai phiên bản CPU.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n[EXCEPTION] Lỗi khởi tạo:");
                
                // --- ĐOẠN CODE MỚI ĐỂ SOI LỖI "REFLECTION" ---
                if (ex is ReflectionTypeLoadException typeLoadEx)
                {
                    Console.WriteLine("--> LỖI THIẾU FILE PHỤ THUỘC (DEPENDENCY):");
                    foreach (var loaderEx in typeLoadEx.LoaderExceptions)
                    {
                        if (loaderEx != null)
                        {
                            Console.WriteLine($"   - Thiếu: {loaderEx.Message}");
                        }
                    }
                }
                // ----------------------------------------------
                
                Console.WriteLine($"Type: {ex.GetType().Name}");
                Console.WriteLine($"Msg: {ex.Message}");
                return false;
            }
        }

        // --- HÀM CHÍNH: UPDATE FIRMWARE ---
        public void ExecuteFirmwareUpdate(string targetIp, string netCardName)
        {
            // Bước 1: Khởi tạo
            if (!Initialize()) return;

            Console.WriteLine($"\n[PROCESS] Chuẩn bị Update Firmware cho IP: {targetIp}...");

            try
            {
                // Bước 2: Chọn Card mạng (Dùng dynamic)
                var interfaces = _satTool.QueryNetworkInterfaces();
                dynamic myInterface = null;

                // Tìm card mạng theo tên
                foreach (var nic in interfaces)
                {
                    string name = nic.Name;
                    string desc = nic.Description;
                    if (name.Contains(netCardName) || desc.Contains(netCardName))
                    {
                        myInterface = nic;
                        break;
                    }
                }

                if (myInterface == null)
                {
                    ConsoleUI.PrintResult("Lỗi: Không tìm thấy Card mạng đã chọn trong SAT.");
                    return;
                }

                // Bước 3: Quét mạng tìm PLC
                Console.WriteLine(">> Đang quét thiết bị trên mạng (Vui lòng đợi 5-10s)...");
                var scanResult = myInterface.ScanNetworkDevices();
                
                dynamic targetDevice = null;

                // Logic tìm IP chấp nhận mọi cấu trúc dữ liệu
                foreach (var dev in scanResult)
                {
                    try 
                    {
                        // Thử duyệt qua danh sách IP
                        foreach (var ip in dev.IPAddresses)
                        {
                            if (ip.ToString() == targetIp) { targetDevice = dev; break; }
                        }
                    }
                    catch 
                    {
                        // Nếu thất bại, thử lấy IP đơn
                        if (dev.IPAddress != null && dev.IPAddress.ToString() == targetIp) targetDevice = dev;
                    }
                    if (targetDevice != null) break;
                }

                if (targetDevice == null)
                {
                    ConsoleUI.PrintResult($"Lỗi: Không tìm thấy PLC có IP {targetIp}. Hãy kiểm tra dây mạng!");
                    return;
                }

                // Bước 4: Thực hiện quy trình Update
                PerformUpdateProcess(targetDevice);
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintResult($"Lỗi trong quá trình thực thi: {ex.Message}");
            }
        }

        private void PerformUpdateProcess(dynamic currentCPU)
        {
            // Bước 4a: Nhập file
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Nhập đường dẫn file Firmware (.upd): ");
            Console.ResetColor();
            
            var udpFile = Console.ReadLine()?.Trim();
            // Xử lý nếu người dùng copy paste có dấu ngoặc kép
            if (udpFile != null && udpFile.StartsWith("\"") && udpFile.EndsWith("\"")) 
                udpFile = udpFile.Substring(1, udpFile.Length - 2);

            if (!File.Exists(udpFile))
            {
                ConsoleUI.PrintResult("Lỗi: File không tồn tại!");
                return;
            }

            // Bước 4b: Validate file
            Console.WriteLine(">> Đang kiểm tra file Firmware...");
            try
            {
                // Chọn thiết bị để thao tác
                currentCPU.Selected = true; 
                
                dynamic result = currentCPU.SetFirmwareFile(udpFile);

                if (!result.Succeeded)
                {
                    ConsoleUI.PrintResult($"Lỗi File: {GetErrorString(result)}");
                    return;
                }

                // Bước 4c: Xác nhận lần cuối
                Console.WriteLine();
                Console.BackgroundColor = ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(" CẢNH BÁO: PLC SẼ DỪNG (STOP) VÀ KHỞI ĐỘNG LẠI! ");
                Console.ResetColor();
                Console.Write("Gõ 'YES' để bắt đầu Update ngay: ");
                
                string confirm = Console.ReadLine()?.Trim().ToUpper();
                if (confirm != "YES" && confirm != "Y")
                {
                    Console.WriteLine("Đã hủy bỏ.");
                    return;
                }

                // Bước 4d: CHẠY UPDATE
                Console.WriteLine("\n>> ĐANG UPDATE FIRMWARE... TUYỆT ĐỐI KHÔNG TẮT ĐIỆN!");
                Console.WriteLine("(Quá trình có thể mất 1-3 phút. Đèn PLC sẽ nháy đỏ/cam)");

                // Gọi lệnh Update: ID, true = Update cả module con
                dynamic updateResult = currentCPU.FirmwareUpdate(currentCPU.ID, true);

                Console.WriteLine();
                if (updateResult.Succeeded)
                {
                    ConsoleUI.PrintResult("THÀNH CÔNG: Firmware đã được cập nhật! PLC đang khởi động lại.");
                }
                else
                {
                    ConsoleUI.PrintResult($"THẤT BẠI: {GetErrorString(updateResult)}");
                }
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintResult($"Lỗi nghiêm trọng khi Update: {ex.Message}");
                Console.WriteLine("Gợi ý: Kiểm tra xem file 'wrapper_native.dll' có nằm cạnh file .exe không?");
            }
        }

        // Hàm lấy thông báo lỗi an toàn từ đối tượng dynamic
        private string GetErrorString(dynamic resultObj)
        {
            try 
            { 
                return $"{resultObj.ErrorName} - {resultObj.Error}"; 
            }
            catch 
            { 
                return "Unknown Error (Không thể đọc mã lỗi)"; 
            }
        }
    }
}