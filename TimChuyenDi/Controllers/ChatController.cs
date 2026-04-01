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
        public async Task<IActionResult> SendMessage(string userMessage, string history)
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
                    var provinces = _context.Provinces.ToList();
                    var detectedProvinces = provinces.Where(p => userMessage.ToLower().Contains(p.ProvinceName.ToLower())).ToList();
                    bool isTomorrow = userMessage.ToLower().Contains("ngày mai") || userMessage.ToLower().Contains("mai");

                    var tripQuery = _context.Trips
                        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.Driver)
                        .Where(t => t.StartTime > DateTime.Now)
                        .AsQueryable();

                    if (detectedProvinces.Any())
                    {
                        var pIds = detectedProvinces.Select(p => p.ProvinceId).ToList();
                        tripQuery = tripQuery.Where(t => pIds.Contains(t.FromStationNavigation.ProvinceId) || pIds.Contains(t.ToStationNavigation.ProvinceId));
                    }
                    
                    if (isTomorrow)
                    {
                        var tomorrow = DateTime.Now.Date.AddDays(1);
                        tripQuery = tripQuery.Where(t => t.StartTime.Date == tomorrow);
                    }

                    var trips = tripQuery.OrderBy(t => t.StartTime).Take(15).ToList();

                    contextInfo = $"THÔNG TIN KHÁCH HÀNG (Thời gian: {currentTime}):\n";
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
                            contextInfo += $"- Mã {t.TripId}: {t.FromStationNavigation.Province.ProvinceName} → {t.ToStationNavigation.Province.ProvinceName} | Giá: {t.BasePrice:N0}đ | {t.StartTime:dd/MM HH:mm} | Trống: {t.AvaiCapacityKg}kg\n";
                        }
                    } else {
                        contextInfo += "==> HIỆN CHƯA CÓ CHUYẾN XE NÀO KHỚP VỚI LỘ TRÌNH/THỜI GIAN YÊU CẦU TRONG CƠ SỞ DỮ LIỆU. <==\n";
                    }

                    aiInstruction = @"
Bạn là TRỢ LÝ Gió Việt. 
- Hãy sử dụng danh sách 'CÁC CHUYẾN XE PHÙ HỢP' để trả lời.
- QUAN TRỌNG: Nếu người dùng hỏi về một lộ trình cụ thể mà trong phần dữ liệu ghi 'HIỆN CHƯA CÓ CHUYẾN XE NÀO KHỚP', hãy trả lời chính xác câu: 'Vui lòng chờ tuyến đi từ bác tài'. Không tự chế ra chuyến xe khác.
- Nếu thấy chuyến xe khớp, hãy giới thiệu và cung cấp mã chuyến xe.
";
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


            string aiReply = await _geminiService.SendMessageAsync(finalPrompt);

            return Json(new { success = true, reply = aiReply });
        }
    }
}