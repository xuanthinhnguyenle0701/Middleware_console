using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json; // Vẫn cần cái này cho các class ChatContent/ChatPart

namespace Middleware_console
{
    public static class HistoryManager
    {
        // Lưu đuôi .txt để bạn dễ click đúp mở bằng Notepad xem code
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chat_history.txt");

        private const string TAG_USER = "<|ROLE:USER|>";
        private const string TAG_MODEL = "<|ROLE:MODEL|>";

        // --- 1. CONVERT TỪ OBJECT -> FILE TOON (SAVE) ---
        public static void Save(List<ChatContent> history)
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                foreach (var msg in history)
                {
                    // Chọn thẻ Tag
                    string tag = (msg.role == "user") ? TAG_USER : TAG_MODEL;
                    
                    sb.AppendLine(tag);
                    
                    if (msg.parts != null && msg.parts.Count > 0)
                    {
                        // Trim() để xóa khoảng trắng thừa đầu đuôi
                        sb.AppendLine(msg.parts[0].text.Trim());
                    }
                    
                    sb.AppendLine();
                    sb.AppendLine("----------------------------------------"); // Kẻ dòng cho đẹp
                    sb.AppendLine();
                }

                // Ghi đè file cũ
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"[History Save Error] {ex.Message}");
            }
        }

        // --- 2. CONVERT TỪ FILE TOON -> OBJECT (LOAD) ---
        public static List<ChatContent> Load()
        {
            var history = new List<ChatContent>();
            if (!File.Exists(FilePath)) return history;

            try
            {
                string rawContent = File.ReadAllText(FilePath, Encoding.UTF8);

                // Regex để cắt chuỗi dựa vào thẻ Tag
                string pattern = $@"({Regex.Escape(TAG_USER)}|{Regex.Escape(TAG_MODEL)})";
                string[] segments = Regex.Split(rawContent, pattern);

                // Duyệt qua mảng kết quả (Bước nhảy là 2 vì Regex.Split trả về cả Tag và Nội dung)
                for (int i = 1; i < segments.Length; i += 2)
                {
                    if (i + 1 >= segments.Length) break;

                    string currentTag = segments[i];
                    string currentText = segments[i + 1];

                    // Làm sạch nội dung (Xóa dòng kẻ ngang)
                    currentText = CleanText(currentText);

                    string role = (currentTag == TAG_USER) ? "user" : "model";

                    history.Add(new ChatContent
                    {
                        role = role,
                        parts = new List<ChatPart> { new ChatPart { text = currentText } }
                    });
                }
            }
            catch (Exception ex)
            {
                ConsoleUI.PrintError($"[History Load Error] {ex.Message}");
            }
            return history;
        }

        // Hàm phụ: Xóa các dòng kẻ trang trí khi load lên RAM
        private static string CleanText(string input)
        {
            var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            StringBuilder sb = new StringBuilder();
            foreach (var line in lines)
            {
                if (line.Contains("-------")) continue; 
                if (string.IsNullOrWhiteSpace(line)) continue;
                sb.AppendLine(line);
            }
            return sb.ToString().Trim();
        }

        public static void Clear()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
    }
}