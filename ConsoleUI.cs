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

        // --- 2. MÔ PHỎNG COMBO BOX (Menu chọn số) ---
        // Thay vì bấm mũi tên xổ xuống, user sẽ nhập số 1, 2, 3...
        public static string SelectOption(string prompt, string[] options)
        {
            Console.WriteLine(prompt);
            for (int i = 0; i < options.Length; i++)
            {
                // In ra: [1] Siemens, [2] Schneider...
                Console.WriteLine($"   [{i + 1}] {options[i]}");
            }

            while (true)
            {
                Console.Write("   Your choice (number): ");
                string input = Console.ReadLine();

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
        public static string GetInput(string prompt, bool allowEmpty = false)
        {
            while (true)
            {
                Console.Write($"{prompt} ");
                string input = Console.ReadLine().Trim();

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
            Console.WriteLine($"{prompt} (Type 'END' on a new line to finish):");
            Console.ForegroundColor = ConsoleColor.White; // Màu chữ trắng cho dễ nhìn

            StringBuilder sb = new StringBuilder();
            while (true)
            {
                Console.Write("   > ");
                string line = Console.ReadLine();

                // Điều kiện thoát nhập liệu
                if (line?.Trim().ToUpper() == "END")
                    break;

                sb.AppendLine(line);
            }
            Console.ResetColor();

            string result = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(result))
                return "No logic provided.";

            return result;
        }

        // --- 5. MÔ PHỎNG LOADING SPINNER (Hiệu ứng chờ) ---
        // Hàm này sẽ chạy animation xoay xoay trong khi đợi Task xử lý xong
        public static async Task ShowSpinner(Task processingTask)
        {
            Console.CursorVisible = false;
            Console.ForegroundColor = ConsoleColor.Yellow;
            char[] spinner = { '|', '/', '-', '\\' };
            int counter = 0;
            Console.Write("Processing... ");
            Console.ResetColor();

            // Lặp cho đến khi Task bên kia chạy xong
            while (!processingTask.IsCompleted)
            {
                Console.Write(spinner[counter % 4]);
                // Lùi con trỏ lại 1 đơn vị để ghi đè ký tự cũ
                Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                counter++;
                await Task.Delay(100);
            }

            Console.Write("Done!   "); // Xóa ký tự spinner cuối cùng
            Console.WriteLine();
            Console.CursorVisible = true;
        }
        // Hàm in thông tin (Màu Cyan - Xanh lơ)
        public static void PrintInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"   [INFO] {message}");
            Console.ResetColor();
        }
        // --- HÀM PHỤ TRỢ: IN MÀU TỰ ĐỘNG ---
        public static void PrintResult(string msg)
        {
            Console.WriteLine(); // Xuống dòng cho thoáng
            if (string.IsNullOrEmpty(msg)) return;

            // Tự động chọn màu dựa vào nội dung thông báo
            if (msg.ToUpper().Contains("SUCCESS"))
            {
                Console.ForegroundColor = ConsoleColor.Green; // Xanh lá
            }
            else if (msg.ToUpper().Contains("WARNING"))
            {
                Console.ForegroundColor = ConsoleColor.Yellow; // Vàng
            }
            else if (msg.ToUpper().Contains("ERROR") || msg.ToUpper().Contains("FAILED"))
            {
                Console.ForegroundColor = ConsoleColor.Red;   // Đỏ
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Cyan;  // Xanh dương (Tin tức)
            }

            Console.WriteLine(msg);
            Console.ResetColor(); // Trả lại màu mặc định
        }
    }
}