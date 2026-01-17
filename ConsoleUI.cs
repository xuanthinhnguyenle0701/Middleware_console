using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Middleware_console
{
    public static class ConsoleUI
    {
        // --- 1. MÔ PHỎNG LABEL & HEADER (Trang trí) ---
        public static void PrintHeader(string title)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================");
            Console.WriteLine($"             {title.ToUpper()}");
            Console.WriteLine("================================================================");
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void PrintSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[SUCCESS] {message}");
            Console.ResetColor();
        }

        public static void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {message}");
            Console.ResetColor();
        }

        public static void PrintStep(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n>> {message}");
            Console.ResetColor();
        }

        // --- HÀM HỖ TRỢ ĐỌC INPUT CÓ XỬ LÝ ESC ---
        // Trả về null nếu ấn ESC, trả về chuỗi nếu ấn Enter
        private static string ReadInputWithEsc()
        {
            StringBuilder buffer = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Escape)
                {
                    return null; // Tín hiệu hủy
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return buffer.ToString().Trim();
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                        Console.Write("\b \b"); // Xóa ký tự trên màn hình
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    buffer.Append(key.KeyChar);
                    Console.Write(key.KeyChar);
                }
            }
        }

        // --- 2. MÔ PHỎNG COMBO BOX (Menu chọn số) ---
        public static string SelectOption(string prompt, string[] options, bool allowEsc = false)
        {
            Console.WriteLine(prompt);
            for (int i = 0; i < options.Length; i++)
            {
                Console.WriteLine($"   [{i + 1}] {options[i]}");
            }

            while (true)
            {
                Console.Write("   Your choice (number): ");
                
                // SỬA: Dùng hàm đọc phím thay vì ReadLine
                string input = ReadInputWithEsc();

                if (input == null && allowEsc) return null;

                if (input == null && !allowEsc) continue;

                if (int.TryParse(input, out int choice) && choice > 0 && choice <= options.Length)
                {
                    string selected = options[choice - 1];
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"   -> Selected: {selected}");
                    Console.ResetColor();
                    return selected;
                }

                PrintError("Invalid selection. Please try again.");
            }
        }

        // --- 3. MÔ PHỎNG TEXT BOX (Nhập liệu 1 dòng) ---
        public static string GetInput(string prompt, bool allowEmpty = false, bool allowEsc = false)
        {
            while (true)
            {
                // Chỉ hiện nhắc nhở ESC nếu cho phép
                string promptText = allowEsc ? $"{prompt} [ESC to Back] > " : $"{prompt} > ";
                Console.Write(promptText);
                
                string input = ReadInputWithEsc();

                // Nếu ESC được phép -> Trả về null
                if (input == null && allowEsc) return null;
                
                // Nếu ESC không được phép -> Bỏ qua
                if (input == null && !allowEsc) continue;

                if (allowEmpty || !string.IsNullOrWhiteSpace(input))
                {
                    return input;
                }
                PrintError("Input cannot be empty!");
            }
        }

        // --- 4. MÔ PHỎNG RICH TEXT BOX (Nhập logic nhiều dòng) ---
        public static string GetMultiLineInput(string prompt)
        {
            Console.WriteLine($"{prompt} (Type 'END' on a new line to finish, ESC to Cancel):");
            Console.ForegroundColor = ConsoleColor.White;

            StringBuilder sb = new StringBuilder();
            
            // Lưu ý: Logic nhập nhiều dòng phức tạp hơn với ESC, 
            // ở đây ta giữ đơn giản: Nếu dòng đầu tiên ấn ESC -> Hủy.
            while (true)
            {
                Console.Write("   > ");
                // Ở đây vẫn dùng ReadLine cho đơn giản, nếu bạn muốn ESC thoát ngay thì cần viết lại logic KeyChar phức tạp hơn
                // Tạm thời giữ nguyên hoặc dùng ReadInputWithEsc cho từng dòng
                string line = ReadInputWithEsc(); 

                if (line == null) return null; // ESC

                if (line.Trim().ToUpper() == "END")
                    break;

                sb.AppendLine(line);
            }
            Console.ResetColor();

            string result = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(result))
                return "No logic provided.";

            return result;
        }

        // --- 5. MÔ PHỎNG LOADING SPINNER ---
        public static async Task ShowSpinner(Task processingTask)
        {
            Console.CursorVisible = false;
            Console.ForegroundColor = ConsoleColor.Yellow;
            char[] spinner = { '|', '/', '-', '\\' };
            int counter = 0;
            Console.Write("Processing... ");
            Console.ResetColor();

            while (!processingTask.IsCompleted)
            {
                Console.Write(spinner[counter % 4]);
                Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                counter++;
                await Task.Delay(100);
            }

            Console.Write("Done!   ");
            Console.WriteLine();
            Console.CursorVisible = true;
        }
    }
}