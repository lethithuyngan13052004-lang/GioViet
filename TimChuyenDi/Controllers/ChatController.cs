using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimChuyenDi.Services;
using TimChuyenDi.Models; // Khai báo để dùng ApplicationDbContext
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

        // "Tiêm" cả GeminiService và ApplicationDbContext vào Controller
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

            // 1. Phân loại theo Role (Customer vs Driver)
            var userIdClaim = User.FindFirstValue("UserId");
            var roleClaim = User.FindFirstValue(System.Security.Claims.ClaimTypes.Role);
            
            string contextInfo = "";
            string aiInstruction = "";

            if (int.TryParse(userIdClaim, out int userId))
            {
                if (roleClaim == "1") // QUẢN TRỊ VIÊN (ADMIN)
                {
<<<<<<< HEAD
                    // Thêm chữ "Mã chuyến xe" rõ ràng để AI lấy dữ liệu ghép vào Link
                    dbContextInfo += $"- Mã chuyến xe: {t.TripId} | Tuyến: {t.FromStationNavigation.Province} đi {t.ToStationNavigation.Province} | Giá mở đầu: {t.BasePrice}đ | Khởi hành: {t.StartTime:dd/MM/yyyy HH:mm} | Tài xế: {t.Driver.Name} | Chỗ trống: {t.AvaiCapacityKg}kg.\n";
=======
                    int pendingVehicles = _context.Vehicles.Count(v => v.Status == 0);
                    int totalUsers = _context.Users.Count();
                    int activeTrips = _context.Trips.Count(t => t.StartTime > DateTime.Now);
                    int totalOrders = _context.Shiprequests.Count();

                    contextInfo = $@"
BẠN ĐANG HỖ TRỢ QUẢN TRỊ VIÊN (ADMIN) HỆ THỐNG.
THỐNG KÊ NHANH:
- Tổng số người dùng: {totalUsers}
- Tổng số đơn hàng: {totalOrders}
- Chuyến xe đang chạy: {activeTrips}
- Xe đang chờ duyệt: {pendingVehicles} (QUAN TRỌNG)
";
                    aiInstruction = @"
Bạn là Trợ lý Quản trị Gió Việt.
Nhiệm vụ: Giúp Admin quản lý hệ thống hiệu quả.
1. BÁO CÁO: Tóm tắt các con số thống kê khi được hỏi.
2. NHẮC NHỞ: Nếu có 'Xe đang chờ duyệt', hãy nhắc Admin kiểm tra trang Quản lý phương tiện.
3. HÀNH ĐỘNG: Cung cấp link nhanh cho Admin:
- Quản lý xe: <br/><a href='/Admin/ManageVehicles' class='btn btn-sm btn-danger fw-bold mt-2 mb-2'>🛠️ Duyệt xe ngay ({pendingVehicles})</a><br/>
- Quản lý chuyến: <br/><a href='/Admin/ManageTrips' class='btn btn-sm btn-dark fw-bold mb-2'>🚚 Xem tất cả chuyến xe</a><br/>";
                }
                else if (roleClaim == "3") // TÀI XẾ
                {
                    // Lấy các chuyến xe của tài xế này
                    var driverTrips = _context.Trips
                        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .Where(t => t.DriverId == userId && t.StartTime > DateTime.Now.AddDays(-1))
                        .ToList();

                    // Lấy các đơn hàng đang chờ ghép (Status = 0 và chưa có TripId)
                    var pendingRequests = _context.Shiprequests
                        .Include(r => r.Cargodetails)
                        .Include(r => r.Shippingroutes).ThenInclude(sr => sr.Request)
                        .Where(r => r.Status == 0 && r.TripId == null)
                        .Take(20)
                        .ToList();

                    contextInfo = "THÀNH PHẦN: BẠN ĐANG LÀ TÀI XẾ.\n\nDANH SÁCH CHUYẾN XE CỦA BẠN:\n";
                    foreach(var dt in driverTrips) {
                        contextInfo += $"- Chuyến #{dt.TripId}: {dt.FromStationNavigation.Province.ProvinceName} -> {dt.ToStationNavigation.Province.ProvinceName} | Khởi hành: {dt.StartTime:dd/MM HH:mm} | Trống: {dt.AvaiCapacityKg}kg.\n";
                    }

                    contextInfo += "\nDANH SÁCH ĐƠN HÀNG ĐANG CHỜ GHÉP (PENDING):\n";
                    foreach(var pr in pendingRequests) {
                        var route = pr.Shippingroutes.FirstOrDefault();
                        var fromP = _context.Provinces.Find(route?.FromProvinceId)?.ProvinceName;
                        var toP = _context.Provinces.Find(route?.ToProvinceId)?.ProvinceName;
                        var weight = pr.Cargodetails.FirstOrDefault()?.Weight ?? 0;
                        contextInfo += $"- Đơn #{pr.Id}: Từ {fromP} đến {toP} | Nặng: {weight}kg | Loại: {pr.Cargodetails.FirstOrDefault()?.Description}.\n";
                    }

                    aiInstruction = @"
Bạn là Trợ lý Điều phối của Gió Việt, đang hỗ trợ TÀI XẾ.
Nhiệm vụ: Giúp tài xế 'GHÉP CHUYẾN' để tối ưu xe.
1. SO SÁNH: Tìm Đơn hàng có Tỉnh đi/đến khớp với Chuyến xe của tài xế.
2. GỢI Ý: 'Tôi thấy đơn #{ID} rất tiện đường cho chuyến #{TripID} của bạn. Bạn muốn nhận không?'
3. NÚT NHẬN ĐƠN: BẮT BUỘC chèn link sau cho mỗi gợi ý:
<br/><a href='/Driver/AcceptGenericOrder?requestId=[ID]&tripId=[TripID]' class='btn btn-sm btn-warning fw-bold mt-2 mb-2'>🚐 Nhận đơn [ID] ngay</a><br/>
4. Nếu tài xế hỏi trống không, hãy chào và liệt kê sơ bộ các đơn hàng tiềm năng.";
                }
                else // KHÁCH HÀNG (Mặc định)
                {
                    var latestOrder = _context.Shiprequests
                        .Include(r => r.Cargodetails)
                        .Include(r => r.Shippingroutes)
                        .Where(r => r.UserId == userId && (r.Status == 0 || r.Status == 1))
                        .OrderByDescending(r => r.Id)
                        .FirstOrDefault();

                    if (latestOrder != null)
                    {
                        var route = latestOrder.Shippingroutes.FirstOrDefault();
                        var cargo = latestOrder.Cargodetails.FirstOrDefault();
                        int? fId = route?.FromProvinceId, tId = route?.ToProvinceId;
                        var fromProv = _context.Provinces.FirstOrDefault(p => p.ProvinceId == fId)?.ProvinceName;
                        var toProv = _context.Provinces.FirstOrDefault(p => p.ProvinceId == tId)?.ProvinceName;

                        contextInfo = $@"
DƯỚI ĐÂY LÀ THÔNG TIN ĐƠN HÀNG MỚI NHẤT CỦA KHÁCH HÀNG:
- Mã đơn hàng: {latestOrder.Id}
- Tuyến đường: Từ {fromProv ?? "Chưa rõ"} đến {toProv ?? "Chưa rõ"}
- Trọng lượng: {cargo?.Weight ?? 0} kg
- Hình thức: {(route?.PickupType == 1 ? "Gửi tại trạm" : "Lấy tại nhà")} -> {(route?.DeliveryType == 1 ? "Nhận tại trạm" : "Giao tận nhà")}
";
                    }

                    var trips = _context.Trips
                        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.Driver)
                        .Where(t => t.StartTime > DateTime.Now)
                        .OrderBy(t => t.StartTime)
                        .Take(30)
                        .ToList();

                    contextInfo += "\nDANH SÁCH CHUYẾN XE ĐANG HOẠT ĐỘNG:\n";
                    foreach (var t in trips)
                    {
                        contextInfo += $"- Mã chuyến xe: {t.TripId} | Tuyến: {t.FromStationNavigation.Province.ProvinceName} -> {t.ToStationNavigation.Province.ProvinceName} | Cước: {t.BasePricePerKg:N0}đ/kg | Khởi hành: {t.StartTime:dd/MM HH:mm} | Trống: {t.AvaiCapacityKg}kg.\n";
                    }

                    aiInstruction = @"
Bạn là Trợ lý Điều phối Gió Việt, đang hỗ trợ KHÁCH HÀNG.
Nhiệm vụ: Phân tích đơn hàng khách và gợi ý chuyến xe phù hợp.
1. SO SÁNH: Chuyến xe có Lộ trình khớp Tỉnh và đủ Chỗ trống.
2. NÚT ĐẶT XE: BẮT BUỘC chèn: <br/><a href='/Customer/BookTrip/[ID]' target='_blank' class='btn btn-sm btn-success fw-bold mt-2 mb-2'>🚐 Đặt chuyến [ID] ngay</a><br/>";
>>>>>>> 436120f49f53790747860d5345472acf0dbc160e
                }
            }

            string secretPrompt = $@"
{aiInstruction}

BỐI CẢNH DỮ LIỆU:
{contextInfo}

LỊCH SỬ TRÒ CHUYỆN:
{history}

Câu hỏi hiện tại: ""{userMessage}""";

            string aiReply = await _geminiService.SendMessageAsync(secretPrompt);
            return Json(new { success = true, reply = aiReply });
        }
    }
}
