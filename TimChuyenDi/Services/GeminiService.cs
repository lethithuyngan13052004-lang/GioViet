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

            // 1. Gọt sạch mọi khoảng trắng (dấu cách, phím enter) vô tình bị dính vào API Key
            string cleanApiKey = _apiKey.Trim();

            // 2. Lắp ráp URL theo cách an toàn nhất (Cộng chuỗi trực tiếp)
            string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key=" + _apiKey.Trim();

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

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();

                    using JsonDocument doc = JsonDocument.Parse(responseJson);
                    var answer = doc.RootElement
                                    .GetProperty("candidates")[0]
                                    .GetProperty("content")
                                    .GetProperty("parts")[0]
                                    .GetProperty("text")
                                    .GetString();

                    return answer;
                }
                else
                {
                    var errorDetail = await response.Content.ReadAsStringAsync();
                    return $"Lỗi kết nối tới AI. Mã lỗi: {response.StatusCode}. Chi tiết: {errorDetail}";
                }
            }
            catch (Exception ex)
            {
                return "Đã xảy ra lỗi hệ thống: " + ex.Message;
            }
        }
    }
}