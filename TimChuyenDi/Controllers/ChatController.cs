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

            if (int.TryParse(userIdClaim, out int userId))
            {
                // ================= ADMIN =================
                if (roleClaim == "1")
                {
                    int pendingVehicles = _context.Vehicles.Count(v => v.Status == 0);
                    int totalUsers = _context.Users.Count();
                    int activeTrips = _context.Trips.Count(t => t.StartTime > DateTime.Now);
                    int totalOrders = _context.Shiprequests.Count();

                    contextInfo = $@"
THỐNG KÊ HỆ THỐNG:
- Người dùng: {totalUsers}
- Đơn hàng: {totalOrders}
- Chuyến đang chạy: {activeTrips}
- Xe chờ duyệt: {pendingVehicles}
";

                    aiInstruction = @"
Bạn là Trợ lý Admin Gió Việt.
- Báo cáo nhanh gọn, rõ ràng.
- Nếu có xe chờ duyệt → nhắc admin xử lý.

Link nhanh:
<a href='/Admin/ManageVehicles' class='btn btn-sm btn-danger'>Duyệt xe</a>
<a href='/Admin/ManageTrips' class='btn btn-sm btn-dark'>Xem chuyến</a>
";
                }

                // ================= DRIVER =================
                else if (roleClaim == "3")
                {
                    var driverTrips = _context.Trips
                        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .Where(t => t.DriverId == userId && t.StartTime > DateTime.Now.AddDays(-1))
                        .ToList();

                    var pendingRequests = _context.Shiprequests
                        .Include(r => r.Cargodetails)
                        .Include(r => r.Shippingroutes)
                        .Where(r => r.Status == 0 && r.TripId == null)
                        .Take(20)
                        .ToList();

                    contextInfo = "CHUYẾN XE CỦA BẠN:\n";
                    foreach (var t in driverTrips)
                    {
                        contextInfo += $"- #{t.TripId}: {t.FromStationNavigation.Province.ProvinceName} → {t.ToStationNavigation.Province.ProvinceName} | {t.StartTime:dd/MM HH:mm} | Trống {t.AvaiCapacityKg}kg\n";
                    }

                    contextInfo += "\nĐƠN HÀNG CHỜ GHÉP:\n";
                    foreach (var r in pendingRequests)
                    {
                        var route = r.Shippingroutes.FirstOrDefault();
                        var weight = r.Cargodetails.FirstOrDefault()?.Weight ?? 0;

                        contextInfo += $"- Đơn #{r.Id} | {weight}kg\n";
                    }

                    aiInstruction = @"
Bạn là trợ lý cho TÀI XẾ.
- Gợi ý đơn tiện đường
- BẮT BUỘC có nút nhận đơn:

<br/><a href='/Driver/AcceptGenericOrder?requestId=[ID]&tripId=[TripID]' 
class='btn btn-sm btn-warning'>Nhận đơn</a><br/>
";
                }

                // ================= CUSTOMER =================
                else
                {
                    var trips = _context.Trips
                        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.Driver)
                        .Where(t => t.StartTime > DateTime.Now)
                        .OrderBy(t => t.StartTime)
                        .Take(30)
                        .ToList();

                    contextInfo = "DANH SÁCH CHUYẾN XE:\n";

                    foreach (var t in trips)
                    {
                        contextInfo += $"- Mã chuyến xe: {t.TripId} | {t.FromStationNavigation.Province.ProvinceName} → {t.ToStationNavigation.Province.ProvinceName} | Giá: {t.BasePrice:N0}đ | {t.StartTime:dd/MM HH:mm} | Trống: {t.AvaiCapacityKg}kg\n";
                    }

                    aiInstruction = @"
Bạn là trợ lý vận tải.
- So sánh chuyến xe
- Tìm chuyến rẻ nhất nếu có kg
- BẮT BUỘC có nút đặt:

<br/><a href='/Customer/BookTrip/[ID]' target='_blank' 
class='btn btn-sm btn-success fw-bold mt-2 mb-2'>🚐 Đặt chuyến [ID]</a><br/>
";
                }
            }

            string finalPrompt = $@"
{aiInstruction}

DỮ LIỆU:
{contextInfo}

LỊCH SỬ:
{history}

Câu hỏi: {userMessage}
";

            string aiReply = await _geminiService.SendMessageAsync(finalPrompt);

            return Json(new { success = true, reply = aiReply });
        }
    }
}