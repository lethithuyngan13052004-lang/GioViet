using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace TimChuyenDi.Services
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        // Hàm khởi tạo: Tự động lấy "chiếc chìa khóa" từ appsettings.json
        public GeminiService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["GeminiAI:ApiKey"];
        }

        // Hàm chính để gửi tin nhắn và nhận câu trả lời
        public async Task<string> SendMessageAsync(string prompt)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                return "Lỗi: Chưa đọc được API Key từ cấu hình.";
            }

            // 1. Gọt sạch mọi khoảng trắng
            string cleanApiKey = _apiKey.Trim();

            // 2. Lắp ráp URL (Sử dụng gemini-2.0-flash để đảm bảo tương thích và có quota tốt nhất 2026)
            string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key=" + cleanApiKey;

            // 3. Đóng gói câu hỏi chuẩn JSON
            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            string jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                // Gọi API
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try 
                    {
                        using JsonDocument doc = JsonDocument.Parse(responseContent);
                        var answer = doc.RootElement
                                        .GetProperty("candidates")[0]
                                        .GetProperty("content")
                                        .GetProperty("parts")[0]
                                        .GetProperty("text")
                                        .GetString();

                        return answer ?? "AI không trả về nội dung.";
                    }
                    catch (Exception jsonEx)
                    {
                        return $"Lỗi xử lý dữ liệu AI (JSON): {jsonEx.Message}. Nội dung gốc: {responseContent}";
                    }
                }
                else
                {
                    return $"Lỗi kết nối tới AI. {response.StatusCode} ({(int)response.StatusCode}). Chi tiết: {responseContent}";
                }
            }
            catch (HttpRequestException httpEx)
            {
                return $"Lỗi kết nối mạng (HTTP): {httpEx.Message}";
            }
            catch (Exception ex)
            {
                return "Đã xảy ra lỗi hệ thống: " + ex.Message;
            }
        }
    }
}