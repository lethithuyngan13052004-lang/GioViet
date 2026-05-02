using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace TimChuyenDi.Services
{
    public class OpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public OpenAIService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["OpenAI:ApiKey"];
        }

        public async Task<string> SendMessageAsync(string prompt)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                return "Lỗi: Chưa đọc được API Key OpenAI từ cấu hình.";
            }

            string url = "https://api.openai.com/v1/chat/completions";

            var requestBody = new
            {
                model = "gpt-4o", // Flagship model, stable and powerful
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.7
            };

            string jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(responseContent);
                        var answer = doc.RootElement
                                        .GetProperty("choices")[0]
                                        .GetProperty("message")
                                        .GetProperty("content")
                                        .GetString();

                        return answer ?? "OpenAI không trả về nội dung.";
                    }
                    catch (Exception jsonEx)
                    {
                        return $"Lỗi xử lý dữ liệu OpenAI (JSON): {jsonEx.Message}. Nội dung gốc: {responseContent}";
                    }
                }
                else
                {
                    return $"Lỗi kết nối tới OpenAI. {response.StatusCode} ({(int)response.StatusCode}). Chi tiết: {responseContent}";
                }
            }
            catch (HttpRequestException httpEx)
            {
                return $"Lỗi kết nối mạng OpenAI (HTTP): {httpEx.Message}";
            }
            catch (Exception ex)
            {
                return "Đã xảy ra lỗi hệ thống khi gọi OpenAI: " + ex.Message;
            }
        }

        public async Task<string> SummarizeDriverReviewsAsync(string reviewsData, string driverName)
        {
            string prompt = $@"Bạn là trợ lý AI phân tích đánh giá chất lượng tài xế. 
Tên tài xế: {driverName}
Đây là danh sách các đánh giá (nội dung bình luận và số sao) của khách hàng về tài xế trong 30 ngày qua:
{reviewsData}

Yêu cầu nhiệm vụ:
1. Đọc và TÓM TẮT một cách khách quan, trung thực dựa trên dữ liệu được cung cấp. Tuyệt đối KHÔNG BỊA ĐẶT hay suy diễn thêm thông tin không có trong danh sách.
2. Nêu bật các điểm mạnh (dựa trên các review tốt).
3. Đề xuất cải thiện nếu có (dựa trên các review xấu hoặc góp ý).
4. Tính điểm trung bình (nếu có thể tự ước lượng từ dữ liệu) và tổng số nhận xét.
5. Nếu số lượng đánh giá quá ít (ví dụ dưới 5 cái) hoặc dữ liệu trống, hãy tóm tắt ngắn gọn và CÓ LỜI ĐỘNG VIÊN, KHUYẾN KHÍCH tài xế chạy nhiều chuyến hơn để nhận thêm phản hồi tích cực từ khách hàng.

Hãy trình bày một cách chuyên nghiệp, thân thiện và động viên tài xế.";

            return await SendMessageAsync(prompt);
        }
    }
}
