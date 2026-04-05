using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TimChuyenDi.Models;

namespace TimChuyenDi.Services
{
    public class BehaviorService
    {
        private readonly TimchuyendiContext _context;
        private readonly OpenAIService _openAIService;

        public BehaviorService(TimchuyendiContext context, OpenAIService openAIService)
        {
            _context = context;
            _openAIService = openAIService;
        }

        public async Task ExtractAndLogBehaviorAsync(int userId, string message)
        {
            try
            {
                string extractPrompt = $@"
Phân tích tin nhắn sau của người dùng (chỉ trọng tâm vào bối cảnh GỬI HÀNG HOÁ / GHÉP HÀNG LIÊN TỈNH): '{message}'. 
Trích xuất sở thích gửi hàng (Like), nỗi lo/điều không thích (Dislike), đặc thù/thói quen gửi hàng (Habit) của khách.
Nếu không có thông tin nào đặc trưng, CHỈ trả lời đúng chữ: NONE.
Nếu có, hãy trả về MỘT mảng JSON duy nhất (không bọc markdown, không giải thích). Mảng chứa các đối tượng có cấu trúc chính xác như sau:
[
  {{ ""Action"": ""Habit"", ""Object"": ""Loại hàng hóa"", ""Value"": ""Hải sản đông lạnh"" }},
  {{ ""Action"": ""Like"", ""Object"": ""Thời gian"", ""Value"": ""Giao buổi tối"" }},
  {{ ""Action"": ""Dislike"", ""Object"": ""Bảo quản"", ""Value"": ""Hàng bị móp méo"" }}
]
";
                string rawExtract = await _openAIService.SendMessageAsync(extractPrompt);
                
                if (!string.IsNullOrWhiteSpace(rawExtract) && !rawExtract.Contains("NONE"))
                {
                    string cleanJson = rawExtract.Replace("```json", "").Replace("```", "").Trim();
                    var behaviors = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(cleanJson);
                    
                    if (behaviors != null)
                    {
                        foreach (var b in behaviors)
                        {
                            if (b.TryGetValue("Action", out string action) && 
                                b.TryGetValue("Object", out string obj) && 
                                b.TryGetValue("Value", out string val))
                            {
                                bool exists = await _context.Behaviorlogs.AnyAsync(x => x.UserId == userId && x.Action == action && x.Object == obj && x.Value == val);
                                if (!exists)
                                {
                                    _context.Behaviorlogs.Add(new Behaviorlog
                                    {
                                        UserId = userId,
                                        Action = action.Length > 50 ? action.Substring(0, 50) : action,
                                        Object = obj.Length > 100 ? obj.Substring(0, 100) : obj,
                                        Value = val.Length > 200 ? val.Substring(0, 200) : val,
                                        CreatedAt = DateTime.Now
                                    });
                                }
                            }
                        }
                        await _context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // Silent fail for background tasks
                System.Diagnostics.Debug.WriteLine("Behavior Extraction Error: " + ex.Message);
            }
        }
    }
}
