using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimChuyenDi.Services;
using TimChuyenDi.Models;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Security.Claims;

namespace TimChuyenDi.Controllers
{
    public class ChatController : Controller
    {
        private readonly GeminiService _geminiService;
        private readonly TimchuyendiContext _context;

        public ChatController(GeminiService geminiService, TimchuyendiContext context)
        {
            _geminiService = geminiService;
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(string userMessage, string history, double? lat, double? lng)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userMessage))
                {
                    return Json(new { success = false, reply = "Vui lòng nhập tin nhắn." });
                }

                var userIdClaim = User.FindFirstValue("UserId");
                var roleClaim = User.FindFirstValue(ClaimTypes.Role);

                string contextInfo = "";
                string aiInstruction = "";
                string currentTime = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

                if (int.TryParse(userIdClaim, out int userId))
                {
                    // ================= ADMIN =================
                    if (roleClaim == "1")
                    {
                        int pendingVehicles = _context.Vehicles.Count(v => v.Status == 0);
                        int totalUsers = _context.Users.Count();
                        int adminCount = _context.Users.Count(u => u.Role == 1);
                        int customerCount = _context.Users.Count(u => u.Role == 2);
                        int driverCount = _context.Users.Count(u => u.Role == 3);
                        int activeTrips = _context.Trips.Count(t => t.StartTime > DateTime.Now);
                        int totalOrders = _context.Shiprequests.Count();

                        contextInfo = $@"
THỐNG KÊ HỆ THỐNG (Báo cáo lúc {currentTime}):
- Tổng người dùng: {totalUsers} (Admin: {adminCount}, Khách: {customerCount}, Tài xế: {driverCount})
- Tổng đơn hàng: {totalOrders}
- Chuyến đang chạy: {activeTrips}
- Xe chờ duyệt: {pendingVehicles}
";

                        aiInstruction = @"
Bạn là TRỢ LÝ QUẢN TRỊ VIÊN Gió Việt. 
- Bạn có quyền truy cập vào các con số thống kê vận hành. 
- Hãy báo cáo ngắn gọn, chuyên nghiệp. 
- Luôn nhắc nhở Admin xử lý các phương tiện đang chờ duyệt (nếu có).
";
                    }

                    // ================= DRIVER =================
                    else if (roleClaim == "3")
                    {
                        var driverTrips = _context.Trips
                            .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                            .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                            .Where(t => t.DriverId == userId && t.StartTime > DateTime.Now.AddDays(-7))
                            .OrderByDescending(t => t.StartTime)
                            .Take(5)
                            .ToList();

                        var myVehicles = _context.Vehicles
                             .Include(v => v.VehicleType)
                             .Where(v => v.DriverId == userId)
                             .ToList();

                        contextInfo = $"THÔNG TIN TÀI XẾ (Thời gian: {currentTime}):\n";
                        contextInfo += "PHƯƠNG TIỆN CỦA BẠN:\n";
                        foreach(var v in myVehicles) {
                            contextInfo += $"- {v.VehicleType.TypeName} | Biển: {v.PlateNumber} | Trạng thái: {(v.Status == 1 ? "Đã duyệt" : "Chờ duyệt")}\n";
                        }

                        contextInfo += "\nCÁC CHUYẾN XE GẦN ĐÂY/SẮP TỚI:\n";
                        foreach (var t in driverTrips)
                        {
                            contextInfo += $"- Mã #{t.TripId}: {t.FromStationNavigation.Province.ProvinceName} → {t.ToStationNavigation.Province.ProvinceName} | {t.StartTime:dd/MM HH:mm} | Trống {t.AvaiCapacityKg}kg\n";
                        }


                        aiInstruction = @"
Bạn là TRỢ LÝ ĐIỀU PHỐI (Dành cho Tài xế). 
- Hãy giúp tài xế quản lý phương tiện và theo dõi chuyến đi.
- Nếu tài xế hỏi về xe chưa được duyệt, hãy bảo họ kiên nhẫn chờ Admin.
- Bạn có thể gợi ý họ kiểm tra danh sách 'Tìm đơn hàng' để tìm thêm hàng ghép.
";
                    }

                    // ================= CUSTOMER =================
                    else
                    {
                        var myOrders = _context.Shiprequests
                            .Include(r => r.Cargodetails)
                            .Include(r => r.Shippingroutes)
                            .Where(r => r.UserId == userId)
                            .OrderByDescending(r => r.Id)
                            .Take(5)
                            .ToList();

                        // --- Thuật toán tìm kiếm thông minh dựa trên tin nhắn người dùng ---
                        var uMsg = userMessage.ToLower()
                            .Replace(" hn ", " hà nội ")
                            .Replace("hcm", "hồ chí minh")
                            .Replace(" sg ", " hồ chí minh ")
                            .Replace(" nd ", " nam định ")
                            .Replace(" hp ", " hải phòng ")
                            .Replace(" đn ", " đà nẵng ")
                            .Replace(" dn ", " đà nẵng ");
                        
                        // Để thay thế chữ bị nén ở đầu/cuối chuỗi
                        if (uMsg.StartsWith("hn ")) uMsg = "hà nội " + uMsg.Substring(3);
                        if (uMsg.EndsWith(" hn")) uMsg = uMsg.Substring(0, uMsg.Length - 3) + " hà nội";
                        if (uMsg.StartsWith("nd ")) uMsg = "nam định " + uMsg.Substring(3);
                        if (uMsg.EndsWith(" nd")) uMsg = uMsg.Substring(0, uMsg.Length - 3) + " nam định";

                        var provinces = _context.Provinces.ToList();
                        var detectedProvinces = provinces.Where(p => 
                        {
                            var normalizedPName = p.ProvinceName.ToLower().Replace("tỉnh ", "").Replace("thành phố ", "").Replace("tp ", "").Trim();
                            return uMsg.Contains(normalizedPName);
                        }).ToList();
                        var pIdFromProvinces = detectedProvinces.Select(p => p.ProvinceId).ToList();

                        var stations = _context.Stations.ToList();
                        var detectedStations = stations.Where(s => uMsg.Contains(s.StationName.ToLower())).ToList();
                        var pIdFromStations = detectedStations.Select(s => s.ProvinceId).ToList();

                        var combinedProvinceIds = pIdFromProvinces.Union(pIdFromStations).Distinct().ToList();
                        
                        Station nearestStation = null;
                        double minDistance = double.MaxValue;

                        // Nếu có tọa độ GPS, quét tìm trạm gần nhất
                        if (lat.HasValue && lng.HasValue)
                        {
                            foreach (var s in stations)
                            {
                                if (s.Latitude.HasValue && s.Longitude.HasValue)
                                {
                                    double dist = GetDistance(lat.Value, lng.Value, (double)s.Latitude.Value, (double)s.Longitude.Value);
                                    if (dist < minDistance)
                                    {
                                        minDistance = dist;
                                        nearestStation = s;
                                    }
                                }
                            }
                        }

                        // Nếu tìm thấy trạm gần nhất (mở rộng tối đa 100km nếu ko tìm đc trong 30km)
                        if (nearestStation != null && minDistance < 100)
                        {
                            // Ưu tiên tin nhắn gõ tay: Nếu khách không nhắc địa điểm nào, ta tự ép lấy tỉnh của Trạm gần nhất
                            if (!combinedProvinceIds.Any())
                            {
                                combinedProvinceIds.Add(nearestStation.ProvinceId);
                            }
                        }

                        bool isTomorrow = userMessage.ToLower().Contains("ngày mai") || userMessage.ToLower().Contains("mai");

                        var tripQuery = _context.Trips
                            .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                            .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                            .Include(t => t.TripStations).ThenInclude(ts => ts.Station).ThenInclude(s => s.Province)
                            .Include(t => t.Driver)
                            .Where(t => t.StartTime > DateTime.Now)
                            .AsQueryable();

                        if (combinedProvinceIds.Any())
                        {
                            foreach (var pId in combinedProvinceIds)
                            {
                                int currentId = pId;
                                tripQuery = tripQuery.Where(t => 
                                    t.FromStationNavigation.ProvinceId == currentId || 
                                    t.ToStationNavigation.ProvinceId == currentId ||
                                    t.TripStations.Any(ts => ts.Station.ProvinceId == currentId)
                                );
                            }
                        }
                        
                        if (isTomorrow)
                        {
                            var tomorrow = DateTime.Now.Date.AddDays(1);
                            tripQuery = tripQuery.Where(t => t.StartTime.Date == tomorrow);
                        }

                        var trips = tripQuery.OrderBy(t => t.StartTime).Take(15).ToList();

                        contextInfo = $"THÔNG TIN KHÁCH HÀNG (Thời gian: {currentTime}):\n";
                        if (nearestStation != null && minDistance < 100)
                        {
                            contextInfo += $"[HỆ THỐNG ĐÃ ĐỊNH VỊ GPS: Khách hàng đang đứng cách trạm '{nearestStation.StationName}' ({nearestStation.Province.ProvinceName}) khoảng {minDistance:F1} km. Hãy ưu tiên gợi ý các chuyến đi qua trạm này hoặc nhắc người dùng mang hàng ra trạm tiện nhất.]\n";
                        }

                        contextInfo += "ĐƠN HÀNG CỦA BẠN (5 đơn gần nhất):\n";
                        foreach(var r in myOrders) {
                            var route = r.Shippingroutes.FirstOrDefault();
                            var weight = r.Cargodetails.FirstOrDefault()?.Weight ?? 0;
                            string status = r.Status switch { 0 => "Chờ xác nhận", 1 => "Đã nhận", 2 => "Bị từ chối", 3 => "Đang giao", 4 => "Đã giao", _ => "Đã hủy" };
                            contextInfo += $"- Đơn #MD{r.Id} | Nặng {weight}kg | Trạng thái: {status} | Từ: {route?.PickupAddress ?? "N/A"} Đến: {route?.DeliveryAddress ?? "N/A"}\n";
                        }

                        contextInfo += "\nDANH SÁCH CÁC CHUYẾN XE PHÙ HỢP CÓ TRONG HỆ THỐNG:\n";
                        if (trips.Any()) {
                            foreach (var t in trips) {
                                var intermediateStops = string.Join(" -> ", t.TripStations.OrderBy(ts => ts.StopOrder).Select(ts => $"{ts.Station.StationName}({ts.Station.Province.ProvinceName})").Distinct());
                                var stopsText = string.IsNullOrEmpty(intermediateStops) ? "" : $" (Đi qua: {intermediateStops})";
                                contextInfo += $"- Mã {t.TripId}: {t.FromStationNavigation.StationName} ({t.FromStationNavigation.Province.ProvinceName}) → {t.ToStationNavigation.StationName} ({t.ToStationNavigation.Province.ProvinceName}){stopsText} | Giá: {t.BasePrice:N0}đ | Khởi hành: {t.StartTime:dd/MM HH:mm} | Trống: {t.AvaiCapacityKg}kg\n";
                            }
                        } else {
                            contextInfo += "[Hệ thống báo cáo: Không tìm thấy chuyến xe nào phù hợp với yêu cầu này trên CSDL]\n";
                        }

                        aiInstruction = @"
Bạn là Trợ Gió - viết tắt cho Trợ Lý Gió Việt. 
- Hãy sử dụng danh sách 'CÁC CHUYẾN XE PHÙ HỢP' để trả lời.
- QUAN TRỌNG: Nếu người dùng hỏi về lộ trình cụ thể mà hệ thống báo 'Không tìm thấy chuyến xe nào phù hợp', hãy trả lời chính xác câu: 'Hiện tại Gió Việt chưa có tuyến xe nào phù hợp với lộ trình/thời gian bạn yêu cầu. Vui lòng chờ tuyến đi từ các bác tài ạ.' Tuyệt đối không tự chế ra chuyến xe mới hay nghĩ ra khoảng thời gian 'trong ngày hôm nay'.
- Nếu thấy chuyến xe khớp, hãy giới thiệu và cung cấp tên Tỉnh và mã chuyến xe.
- TUYỆT ĐỐI KHÔNG sử dụng IN HOA TOÀN BỘ CHỮ để nhấn mạnh (Ví dụ không dùng: KHÔNG TÌM THẤY CHUYẾN XE). Bạn được phép dùng in đậm (**) để làm nổi bật các thông tin quan trọng (Tên Tỉnh, Mã Chuyến, Giá Tiền). Hãy trả lời bằng câu văn bình thường, lịch sự.
";

                        if (!lat.HasValue || !lng.HasValue) 
                        {
                            aiInstruction += "\n- LƯU Ý MỞ RỘNG: Hiện tại khách hàng BỊ TẮT ĐỊNH VỊ. Nếu khách đang muốn tìm chuyến xe, hãy khéo léo chèn thêm 1 câu nhắc nhở nhẹ nhàng ở cuối: 'Để tìm chuyến chính xác nhất, bạn có thể bấm nút Vị trí 📍 màu xanh góc trái để mình ưu tiên tìm các trạm gửi hàng gần bạn nhất nhé!'";
                        }
                    }
                }

                else 
                {
                    // Khách chưa đăng nhập
                    aiInstruction = "Bạn là trợ lý giải đáp thắc mắc chung về Gió Việt. Hãy giới thiệu về dịch vụ gửi hàng và ghép xe liên tỉnh.";
                    contextInfo = "Hệ thống vận tải Gió Việt chuyên cung cấp dịch vụ gửi hàng liên tỉnh giá rẻ thông qua việc ghép xe.";
                }

                string finalPrompt = $@"
Hệ thống: Gió Việt (Dịch vụ vận tải/ghép hàng liên tỉnh).
Vai trò: {aiInstruction}
Dữ liệu hiện hành tại hệ thống:
{contextInfo}

Lưu ý quan trọng:
1. Trả lời bằng tiếng Việt, lịch sự, thân thiện.
2. Nếu có mã đơn hàng (#MD...), hãy dùng nó để trả lời khách.
3. Nếu người dùng hỏi về thông tin không có trong 'DỮ LIỆU' hoặc 'LỊCH SỬ', hãy trả lời rằng bạn chưa có thông tin đó và khuyên họ liên hệ hotline 1900 xxxx.

Lịch sử trò chuyện:
{history}

Người dùng hỏi: {userMessage}
";

                var chatTask = _geminiService.SendMessageAsync(finalPrompt);
                Task<string> extractTask = null;

                if (int.TryParse(userIdClaim, out int loggedInUserId))
                {
                    string extractPrompt = $@"
Phân tích tin nhắn sau của người dùng (chỉ trọng tâm vào bối cảnh GỬI HÀNG HOÁ / GHÉP HÀNG LIÊN TỈNH): '{userMessage}'. 
Trích xuất sở thích gửi hàng (Like), nỗi lo/điều không thích (Dislike), đặc thù/thói quen gửi hàng (Habit) của khách.
Nếu không có thông tin nào đặc trưng, CHỈ trả lời đúng chữ: NONE.
Nếu có, hãy trả về MỘT mảng JSON duy nhất (không bọc markdown, không giải thích). Mảng chứa các đối tượng có cấu trúc chính xác như sau:
[
  {{ ""Action"": ""Habit"", ""Object"": ""Loại hàng hóa"", ""Value"": ""Hải sản đông lạnh"" }},
  {{ ""Action"": ""Like"", ""Object"": ""Thời gian"", ""Value"": ""Giao buổi tối"" }},
  {{ ""Action"": ""Dislike"", ""Object"": ""Bảo quản"", ""Value"": ""Hàng bị móp méo"" }}
]
";
                    extractTask = _geminiService.SendMessageAsync(extractPrompt);
                }

                string aiReply = await chatTask;

                if (extractTask != null)
                {
                    try
                    {
                        string rawExtract = await extractTask;
                        if (!string.IsNullOrWhiteSpace(rawExtract) && !rawExtract.Contains("NONE"))
                        {
                            string cleanJson = rawExtract.Replace("```json", "").Replace("```", "").Trim();
                            var behaviors = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(cleanJson);
                            
                            if (behaviors != null)
                            {
                                foreach (var b in behaviors)
                                {
                                    if (b.TryGetValue("Action", out string action) && 
                                        b.TryGetValue("Object", out string obj) && 
                                        b.TryGetValue("Value", out string val))
                                    {
                                        bool exists = await _context.Behaviorlogs.AnyAsync(x => x.UserId == loggedInUserId && x.Action == action && x.Object == obj && x.Value == val);
                                        if (!exists)
                                        {
                                            _context.Behaviorlogs.Add(new Behaviorlog
                                            {
                                                UserId = loggedInUserId,
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
                    catch
                    {
                        // Bỏ qua lỗi trong quá trình phân tích hoặc lưu log (không ảnh hưởng trải nghiệm chat)
                    }
                }

                return Json(new { success = true, reply = aiReply });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, reply = "SYSTEM_ERROR: " + ex.Message + " | Inner: " + ex.InnerException?.Message });
            }
        }
        private double GetDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371; // Bán kính trái đất bằng KM
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c; 
        }
    }
}