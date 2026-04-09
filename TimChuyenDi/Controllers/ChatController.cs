using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimChuyenDi.Services;
using TimChuyenDi.Models;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace TimChuyenDi.Controllers
{
    public class ChatController : Controller
    {
        private readonly OpenAIService _openAIService;
        private readonly TimchuyendiContext _context;
        private readonly BehaviorService _behaviorService;
        private readonly RoutingService _routingService;

        public ChatController(OpenAIService openAIService, TimchuyendiContext context, BehaviorService behaviorService, RoutingService routingService)
        {
            _openAIService = openAIService;
            _context = context;
            _behaviorService = behaviorService;
            _routingService = routingService;
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
                var userDisplayName = User.FindFirstValue("FullName") ?? User.Identity.Name ?? "Quý khách";
                string roleName = roleClaim switch { "1" => "Quản trị viên", "3" => "Tài xế", _ => "Khách hàng" };
                
                bool isFirstMessage = string.IsNullOrWhiteSpace(history);
                
                // --- 1. Lấy dữ liệu danh mục & Cấu hình ---
                var minPriceConfig = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.KeyName == "MinPrice");
                decimal minPrice = minPriceConfig?.Value ?? 0;
                var vwfConfig = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.KeyName == "VolumeToWeightFactor");
                decimal vwf = vwfConfig?.Value ?? 250;

                var allProvinces = _context.Provinces.Select(p => new { p.ProvinceId, p.ProvinceName }).ToList();
                var allCargoTypes = _context.Cargotypes.Select(c => new { c.CargoTypeId, c.TypeName, c.PriceMultiplier }).ToList();
                
                string provinceListText = string.Join(", ", allProvinces.Select(p => $"{p.ProvinceName}(ID:{p.ProvinceId})"));
                string cargoTypeListText = string.Join(", ", allCargoTypes.Select(c => $"{c.TypeName}(ID:{c.CargoTypeId}, Hệ số x{c.PriceMultiplier})"));

                var searchMsg = (history + " " + userMessage).ToLower();
                var uMsg = searchMsg; 
                uMsg = Regex.Replace(uMsg, @"\bhn\b", "hà nội");
                uMsg = Regex.Replace(uMsg, @"\bhcm\b", "hồ chí minh");
                uMsg = Regex.Replace(uMsg, @"\bsg\b", "hồ chí minh");
                uMsg = Regex.Replace(uMsg, @"\bnd\b", "nam định");
                uMsg = Regex.Replace(uMsg, @"\bhp\b", "hải phòng");
                uMsg = Regex.Replace(uMsg, @"\bđn\b|dn\b", "đà nẵng");
                uMsg = Regex.Replace(uMsg, @"\blc\b", "lào cai");
                uMsg = Regex.Replace(uMsg, @"\bth\b", "thanh hóa");

                // --- 2. PHÂN LOẠI Ý ĐỊNH (INTENT DETECTION) & TYPO TOLERANCE ---
                string uMsgLower = uMsg.ToLower();
                bool isShipping = Regex.IsMatch(uMsgLower, @"gui|ship|vận chuyển|chuyển hàng|guiw|shipp|gửi");
                bool isPricing = Regex.IsMatch(uMsgLower, @"giá|gia|cước|tiền|bao nhiêu|gias|cuoc|tien");
                bool isTripSearch = Regex.IsMatch(uMsgLower, @"chuyến|xe|lịch|tim|tìm|chuyen");
                bool isStats = Regex.IsMatch(uMsgLower, @"thống kê|trạng thái|tình hình|báo cáo|thong ke|bao cao");
                bool isConfig = Regex.IsMatch(uMsgLower, @"đổi|sửa|chỉnh|thay đổi|set|config|cấu hình|doi|sua|chinh");
                bool isTracking = Regex.IsMatch(uMsgLower, @"lịch sử|đơn hàng|don hang|lich su|theo dõi|kiem tra");

                // --- 3. ĐỊNH NGHĨA CÁC MODULE PROMPT ---
                string basePersonaPrompt = $@"
Bạn là Trợ Gió - Trợ lý AI thông minh của Gió Việt.
Bạn đang hỗ trợ {roleName} tên: {userDisplayName}. Lần chat này là lúc {currentTime}.
{(isFirstMessage ? "[Đây là tin nhắn đầu tiên, hãy giới thiệu ngắn gọn về Trợ Gió và chào mừng khách.]" : "")}";

                string styleGuidelinePrompt = @"
QUY TẮC PHONG CÁCH:
- NGẮN GỌN & TRỰC TIẾP. Không lặp lại lời chào rườm rà.
- THIẾU GÌ HỎI NẤY. Đi thẳng vào vấn đề.
- TÓM TẮT CHI TIẾT CHỈ KHI XÁC NHẬN qua link CONFIRM.
- KHÔNG hiển thị mã ID hệ thống cho khách. Không dùng CAPSLOCK.
- Hiển thị kết quả dạng VĂN BẢN PHẲNG (PLAIN TEXT). Không dùng Markdown cho link.";

                string sharedPricingLogic = $@"
LOGIC TÍNH GIÁ (Nội bộ):
- Quy đổi: Max(Khối lượng thực, (Dài*Rộng*Cao)/1,000,000 * {vwf}).
- Giá = Max({minPrice}, (BasePrice * ChargeableWeight / Sức chứa) * Hệ số Tuyến * Hệ số Loại hàng).
- QUY TẮC: TUYỆT ĐỐI KHÔNG HIỂN THỊ CÔNG THỨC. Chỉ trả về giá cuối cùng.";

                string adminStatsModule = "NHIỆM VỤ THỐNG KÊ: Báo cáo vận hành dựa trên số liệu thực tế (Tổng đơn, Chuyến đang chạy, Xe chờ duyệt) để nhắc Admin xử lý.";
                string adminConfigModule = @"NHIỆM VỤ CẤU HÌNH: Hỗ trợ Admin sửa đổi hệ thống. Dùng các link: 
- CONFIRM_CONFIG_LINK[key=[KEY]&val=[VALUE]]
- CONFIRM_CARGO_LINK[id=[ID_HOẶC_0]&name=[TÊN]&multi=[HỆ_SỐ]]
- CONFIRM_VEHICLE_LINK[id=[ID_HOẶC_0]&name=[TÊN]&desc=[MÔ_TẢ]]
- CONFIRM_TRIPTYPE_LINK[id=[ID_HOẶC_0]&type=[TÊN]&multi=[HỆ_SỐ]]";

                string driverTripCreateModule = @"NHIỆM VỤ ĐĂNG CHUYẾN: Hướng dẫn tài xế qua 5 bước: Chọn xe -> Chọn lộ trình (gợi ý trạm gần nhất qua GPS) -> Loại hình/Trạm dừng -> Thời gian/Giá -> CONFIRM_TRIP_LINK.";
                string driverVehicleModule = "NHIỆM VỤ XE: Liệt kê danh sách xe của tài xế và trạng thái duyệt (ID xe, Biển số, Loại xe).";

                string customerOrderModule = @"NHIỆM VỤ ĐẶT ĐƠN: 
- TỰ ĐỘNG ánh xạ hàng hóa (vị dụ: Hải sản -> Thực phẩm tươi).
- Nếu là thực phẩm tươi mà không có xe đông lạnh, phải CẢNH BÁO BẢO QUẢN.
- Luôn hỏi Cân nặng và Kích thước nếu chưa rõ.
- Nhắc phí lấy hàng tận nơi có thể phát sinh.
- Hiển thị: CONFIRM_LINK[fromId=[ID_TỈNH_ĐI]&toId=[ID_TỈNH_ĐẾN]&weight=[KG]&l=[DÀI]&w=[RỘNG]&h=[CAO]&desc=[LOẠI_HÀNG]&phone=[SDT_NHẬN]&pType=[2_BẾN_1_NHÀ]&dType=[2_BẾN_1_NHÀ]&pAddr=[ĐC_ĐI]&dAddr=[ĐC_ĐẾN]&tripId=[MÃ_CHUYẾN]]";
                
                string customerTrackingModule = "NHIỆM VỤ THEO DÕI: Liệt kê trạng thái các đơn hàng gần nhất của khách (Chờ xác nhận, Đang giao, Đã hủy...).";

                // --- 4. LẤY DỮ LIỆU & XÂY DỰNG AI INSTRUCTION/CONTEXT (CHẠY NỐI TIẾP) ---
                aiInstruction += basePersonaPrompt + styleGuidelinePrompt;

                if (int.TryParse(userIdClaim, out int userId))
                {
                    if (roleClaim == "1") // Admin
                    {
                        var sysConfigs = await _context.SystemConfigs.ToListAsync();
                        int pendingVehicles = await _context.Vehicles.CountAsync(v => v.Status == 0);
                        int activeTrips = await _context.Trips.CountAsync(t => t.StartTime > DateTime.Now);
                        int totalOrders = await _context.Shiprequests.CountAsync();
                        
                        contextInfo += $"\nSTATS: {totalOrders} đơn, {activeTrips} chuyến, {pendingVehicles} xe chờ duyệt.";
                        contextInfo += "\nCONFIGS: " + string.Join(", ", sysConfigs.Select(c => $"{c.KeyName}={c.Value}"));

                        if (isStats || isFirstMessage) aiInstruction += "\n" + adminStatsModule;
                        if (isConfig) aiInstruction += "\n" + adminConfigModule;
                    }
                    else if (roleClaim == "3") // Driver
                    {
                        var driverTrips = await _context.Trips.Include(t=>t.FromStationNavigation).Include(t=>t.ToStationNavigation).Where(t=>t.DriverId == userId).OrderByDescending(t=>t.StartTime).Take(5).ToListAsync();
                        var myVehicles = await _context.Vehicles.Include(v=>v.VehicleType).Where(v=>v.DriverId == userId).ToListAsync();

                        contextInfo += "\nXE CỦA BẠN: " + string.Join(", ", myVehicles.Select(v => $"{v.VehicleType.TypeName} ({v.PlateNumber}) - ID:{v.VehicleId}"));
                        contextInfo += "\nCHUYẾN CỦA BẠN: " + string.Join(", ", driverTrips.Select(t => $"#{t.TripId}"));

                        aiInstruction += "\n" + driverTripCreateModule;
                        if (isTripSearch || isFirstMessage) aiInstruction += "\n" + driverVehicleModule;
                    }
                    else // Customer
                    {
                        var myOrders = await _context.Shiprequests.Include(r => r.Shippingroutes).Where(r => r.UserId == userId).OrderByDescending(r => r.Id).Take(5).ToListAsync();
                        contextInfo += "\nĐƠN HÀNG CỦA BẠN: " + string.Join(", ", myOrders.Select(r => $"#MD{r.Id} ({r.Status})"));

                        aiInstruction += "\n" + sharedPricingLogic;
                        if (isShipping || isFirstMessage) aiInstruction += "\n" + customerOrderModule;
                        if (isTracking) aiInstruction += "\n" + customerTrackingModule;
                        if (isPricing) aiInstruction += "\n" + "Hãy tính giá ước tính cho khách dựa trên thông tin hàng hóa cung cấp.";
                    }
                }
                else 
                {
                    contextInfo += "\nKHÁCH VÃNG LAI: Chỉ xem được các chuyến công khai.";
                    aiInstruction += "\n" + sharedPricingLogic + "\n" + customerOrderModule;
                }

                // Tích hợp dữ liệu Search nếu cần
                if (isTripSearch || isShipping)
                {
                    var trips = await _context.Trips.Include(t => t.FromStationNavigation).Include(t => t.ToStationNavigation).Where(t => t.StartTime > DateTime.Now).OrderBy(t => t.StartTime).Take(10).ToListAsync();
                    contextInfo += "\nTRIP DATA: " + string.Join("\n", trips.Select(t => $"Mã {t.TripId}: {t.FromStationNavigation.StationName}->{t.ToStationNavigation.StationName} ({t.StartTime:dd/MM HH:mm})"));
                }

                // Tích hợp GPS logic
                if (lat.HasValue && lng.HasValue)
                {
                    contextInfo += $"\nGPS: Đã có tọa độ ({lat}, {lng}). Ưu tiên tìm trạm gần vị trí này.";
                }
                else
                {
                    aiInstruction += "\nNHẮC GPS: Nếu khách cần tìm chuyến quanh họ, hãy nhắc bấm nút **Vị trí [[GEO_ICON]]**.";
                }

                // --- 5. TỔNG HỢP PROMPT CUỐI CÙNG ---
                string finalPrompt = $@"
VAI TRÒ & HẬU ĐÀI:
{aiInstruction}

DỮ LIỆU HỆ THỐNG HIỆN TẠI (CONTEXT):
{contextInfo}

LƯU Ý QUAN TRỌNG:
- Trả lời bằng tiếng Việt, thân thiện.
- Intent phát hiện: {(isShipping?"Gửi hàng, " : "")}{(isPricing?"Tính giá, " : "")}{(isTripSearch?"Tìm chuyến, " : "")}{(isConfig?"Sửa cấu hình, " : "")}{(isStats?"Báo cáo" : "")}
- Tuyệt đối không Markdown cho link.

LỊCH SỬ CHAT:
{history}

CÂU HỎI MỚI NHẤT: {userMessage}
";

                string aiReply = await _openAIService.SendMessageAsync(finalPrompt);

                // Ghi nhận hành vi khách hàng (Background)
                if (int.TryParse(userIdClaim, out int loggedInUserId))
                {
                    _ = Task.Run(() => _behaviorService.ExtractAndLogBehaviorAsync(loggedInUserId, userMessage));
                }

                return Json(new { success = true, reply = aiReply });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, reply = "SYSTEM_ERROR: " + ex.Message });
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