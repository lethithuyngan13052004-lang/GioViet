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

        public IActionResult Index()
        {
            var role = User.FindFirstValue(ClaimTypes.Role);
            var fullName = User.FindFirstValue("FullName");

            ViewBag.UserRole = role; // "1": Admin, "2": Customer, "3": Driver
            ViewBag.UserDisplayName = fullName ?? User.Identity.Name ?? "Bạn";

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
                uMsg = Regex.Replace(uMsg, @"\bsl\b", "sơn la");

                if (int.TryParse(userIdClaim, out int userId))
                {
                    if (roleClaim == "1") // Admin
                    {
                        var sysConfigs = _context.SystemConfigs.ToList();
                        int pendingVehicles = _context.Vehicles.Count(v => v.Status == 0);
                        int activeTrips = _context.Trips.Count(t => t.StartTime > DateTime.Now);
                        int totalOrders = _context.Shiprequests.Count();

                        contextInfo = $@"
THỐNG KÊ HỆ THỐNG (Báo cáo lúc {currentTime}):
- Tổng đơn hàng: {totalOrders}
- Chuyến đang chạy: {activeTrips}
- Xe chờ duyệt: {pendingVehicles}

CẤU HÌNH HỆ THỐNG HIỆN TẠI:
";
                        foreach(var c in sysConfigs) {
                            contextInfo += $"- {c.KeyName}: {c.Value}\n";
                        }

                        aiInstruction = $@"
Bạn là TRỢ LÝ QUẢN TRỊ VIÊN Gió Việt.
Nhiệm vụ 1: Báo cáo vận hành: Sử dụng các con số thống kê ở trên để nhấc nhở Admin (ví dụ: duyệt xe).
Nhiệm vụ 2: Quản lý cấu hình: 
- Nếu Admin muốn thay đổi một cấu hình (ví dụ: 'Đổi giá tối thiểu thành 400k'), hãy tìm key phù hợp (như 'MinPrice') và cung cấp link xác nhận:
  CONFIRM_CONFIG_LINK[key=[KEY]&val=[VALUE]]
- Chỉ dẫn Admin về ý nghĩa các thông số nếu họ hỏi.
- Luôn giữ thái độ chuyên nghiệp, tôn trọng Quản trị viên.
";
                    }
                    else if (roleClaim == "3") // Driver
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

                        var tripTypes = _context.TripTypes.ToList();
                        string tripTypesText = string.Join(", ", tripTypes.Select(tt => $"{tt.Type}(ID:{tt.IdType})"));

                        // 🔍 SMART STATION DETECTION & GEOCODING
                        Station? nearestStation = null;
                        List<Station> suggestedStations = new List<Station>();
                        
                        // 1. Trộn lat/lng từ tham số (GPS) hoặc thử Geocode địa chỉ trong tin nhắn
                        double? targetLat = (double?)lat;
                        double? targetLng = (double?)lng;
                        string? geocodedName = null;

                        if (!targetLat.HasValue && userMessage.Length > 5)
                        {
                            // Thử geocode nếu tin nhắn dài và có từ khóa địa điểm hoặc tên tỉnh/thành phố
                            bool looksLikeAddress = userMessage.ToLower().Contains("tại") || 
                                                  userMessage.ToLower().Contains("ở") || 
                                                  userMessage.ToLower().Contains("địa chỉ") ||
                                                  allProvinces.Any(p => userMessage.ToLower().Contains(p.ProvinceName.ToLower()));

                            if (looksLikeAddress)
                            {
                                var geoTask = await _routingService.GeocodeAsync(userMessage);
                                targetLat = geoTask.lat;
                                targetLng = geoTask.lng;
                                geocodedName = geoTask.displayName;
                            }
                        }

                        // 2. Nếu có tọa độ, tìm trạm gần nhất
                        if (targetLat.HasValue && targetLng.HasValue)
                        {
                            var allStations = _context.Stations.Include(s => s.Province).ToList();
                            nearestStation = allStations
                                .Where(s => s.Latitude.HasValue && s.Longitude.HasValue)
                                .OrderBy(s => Math.Pow((double)s.Latitude!.Value - targetLat.Value, 2) + 
                                              Math.Pow((double)s.Longitude!.Value - targetLng.Value, 2))
                                .FirstOrDefault();
                            
                            if (nearestStation != null) suggestedStations.Add(nearestStation);
                        }

                        // 3. Fallback: Tìm trạm theo tên/tỉnh/địa chỉ văn bản
                        var textDetected = _context.Stations
                            .Include(s => s.Province)
                            .AsEnumerable()
                            .Where(s => uMsg.Contains(s.StationName.ToLower()) || 
                                       uMsg.Contains(s.Province.ProvinceName.ToLower().Replace("tỉnh ", "").Replace("thành phố ", "").Replace("tp ", "").Trim()) ||
                                       (s.Address != null && uMsg.Contains(s.Address.ToLower())))
                            .Take(10)
                            .ToList();
                        
                        foreach(var s in textDetected) {
                            if (!suggestedStations.Any(ex => ex.StationId == s.StationId))
                                suggestedStations.Add(s);
                        }

                        contextInfo = $"THÔNG TIN TÀI XẾ (Thời gian: {currentTime}):\n";
                        contextInfo += "PHƯƠNG TIỆN CỦA BẠN (Đã duyệt mới có thể dùng):\n";
                        foreach(var v in myVehicles) {
                            contextInfo += $"- ID:{v.VehicleId} | {v.VehicleType.TypeName} | Biển: {v.PlateNumber} (5 số cuối: {v.PlateNumber.Substring(Math.Max(0, v.PlateNumber.Length - 5))}) | Trạng thái: {(v.Status == 1 ? "Đã duyệt" : "Chờ duyệt")}\n";
                        }

                        contextInfo += $"\nLOẠI HÌNH VẬN TẢI: {tripTypesText}\n";
                        
                        if (suggestedStations.Any()) {
                            contextInfo += "\nGỢI Ý TRẠM XE PHÙ HỢP:\n";
                            if (nearestStation != null) {
                                contextInfo += $"[HỆ THỐNG PHÁT HIỆN]: Trạm gần vị trí của bác tại '{geocodedName ?? "tọa độ này"}' nhất là: {nearestStation.StationName} (Địa chỉ: {nearestStation.Address}). HÃY ƯU TIÊN GỢI Ý TRẠM NÀY.\n";
                            }
                            foreach(var s in suggestedStations.Take(15)) {
                                contextInfo += $"- {s.StationName} | Đ/C: {s.Address} | {s.Province.ProvinceName} | ID:{s.StationId}\n";
                            }
                        }

                        contextInfo += "\nCÁC CHUYẾN XE CỦA BẠN GẦN ĐÂY:\n";
                        foreach (var t in driverTrips)
                        {
                            contextInfo += $"- Mã #{t.TripId}: {t.FromStationNavigation.Province.ProvinceName} → {t.ToStationNavigation.Province.ProvinceName} | {t.StartTime:dd/MM HH:mm} | Trống {t.AvaiCapacityKg}kg\n";
                        }

                        aiInstruction = $@"
Bạn là TRỢ LÝ ĐIỀU PHỐI Gió Việt (Dành cho Tài xế).

Nhiệm vụ 1: QUẢN LÝ XE & CHUYẾN ĐI:
- Giúp tài xế theo dõi các chuyến xe sắp tới.

Nhiệm vụ 2: HỖ TRỢ ĐĂNG CHUYẾN XE MỚI:
- Bước 1: CHỌN XE: Mặc định chọn xe đã duyệt nếu chỉ có 1 xe.
- Bước 2: THU THẬP LỘ TRÌNH:
  + Dựa trên địa chỉ tài xế cung cấp hoặc GPS, hãy tìm trạm gần nhất trong danh sách 'GỢI Ý TRẠM XE PHÙ HỢP'.
  + TỰ ĐỘNG CHỌN: Nếu hệ thống báo có '[HỆ THỐNG PHÁT HIỆN]' trạm gần nhất, hãy đề xuất trạm đó ngay: 'Theo em thấy trạm [Tên Trạm] ở [Địa chỉ] là gần chỗ bác nhất, bác chọn trạm này luôn nhé?'.
- Bước 3: LOẠI HÌNH & TRẠM DỪNG: Hỏi 'Chuyến riêng' hay 'Chuyến ghép'. Nếu ghép phải hỏi trạm dừng.
- Bước 4: THỜI GIAN & GIÁ: Hỏi Giờ khởi hành và Giá.
- Bước 5: XÁC NHẬN: Hiển thị link CONFIRM_TRIP_LINK đầy đủ.

QUY TẮC: Thân thiện, Ngắn gọn, TỰ ĐỘNG gợi ý trạm dựa trên địa chỉ/vị trí.
";
                    }
                    else // Customer
                    {
                        var myOrders = _context.Shiprequests
                            .Include(r => r.Cargodetails)
                            .Include(r => r.Shippingroutes)
                            .Where(r => r.UserId == userId)
                            .OrderByDescending(r => r.Id)
                            .Take(5)
                            .ToList();

                        contextInfo = $"THÔNG TIN KHÁCH HÀNG (Thời gian: {currentTime}):\n";
                        contextInfo += "ĐƠN HÀNG CỦA BẠN (5 đơn gần nhất):\n";
                        foreach(var r in myOrders) {
                            var route = r.Shippingroutes.FirstOrDefault();
                            var weight = r.Cargodetails.FirstOrDefault()?.Weight ?? 0;
                            string status = r.Status switch { 0 => "Chờ xác nhận", 1 => "Đã nhận", 2 => "Bị từ chối", 3 => "Đang giao", 4 => "Đã giao", _ => "Đã hủy" };
                            contextInfo += $"- Đơn #MD{r.Id} | Nặng {weight}kg | Trạng thái: {status} | Từ: {route?.PickupAddress ?? "N/A"} Đến: {route?.DeliveryAddress ?? "N/A"}\n";
                        }
                    }
                }
                else 
                {
                    contextInfo = "Hệ thống vận tải Gió Việt chuyên cung cấp dịch vụ gửi hàng liên tỉnh giá rẻ thông qua việc ghép xe.\n";
                    if (isFirstMessage)
                    {
                        contextInfo += "[LƯU Ý: Đây là tin nhắn đầu tiên, hãy giới thiệu ngắn gọn về bản thân là Trợ Gió và chào mừng khách đến với Gió Việt.]\n";
                    }
                }

                string gpsHint = (lat.HasValue && lng.HasValue) 
                    ? "[HỆ THỐNG: Đã có tọa độ GPS của khách, KHÔNG CẦN nhắc khách bấm nút Vị trí nữa.]" 
                    : "- NHẮC NHỞ GPS: 'Để tìm chuyến chính xác nhất xung quanh bạn, hãy bấm nút **Vị trí [[GEO_ICON]]** (màu xanh ở góc trái khung chat)'.";

                aiInstruction = $@"
Bạn là Trợ Gió - Trợ lý AI thông minh của Gió Việt.
Người đang trò chuyện với bạn là {roleName} tên: {userDisplayName}. Hãy xưng hô và hỗ trợ cho đúng tư cách của họ.
{aiInstruction}

QUY TẮC PHONG CÁCH (STYLE GUIDELINES):
- NGẮN GỌN & TRỰC TIẾP: Tránh lặp lại lời chào hỏi hay câu nệ rườm rà ở mỗi lượt chat.
- THIẾU GÌ HỎI NẤY: Nếu chỉ thiếu 1-2 thông tin, hãy vào thẳng vấn đề (Ví dụ: 'Chuyến khởi hành vào mấy giờ bác nhỉ?' thay vì chào lại bác tài).
- TÓM TẮT CHI TIẾT CHỈ KHI XÁC NHẬN: Chỉ liệt kê đầy đủ thông tin khi bạn đã thu thập đủ và chuẩn bị hiển thị link CONFIRM. Còn lại hãy trả lời ngắn gọn nhất có thể.

Nhiệm vụ 1: TƯ VẤN LỘ TRÌNH & HÀNG HÓA:
- Sử dụng 'HỆ THỐNG TRIP DATA' bên dưới để trả lời khách.
- ÁNH XẠ HÀNG HÓA TỰ ĐỘNG: Nếu khách nói 'cá', 'thịt', 'hải sản', 'tôm', 'cua', 'tươi sống', 'thực phẩm sống'... hãy tự hiểu là 'Thực phẩm tươi'. Đừng hỏi lại khách nếu đã rõ.
- ƯU TIÊN & THAY THẾ: 
  + Nếu là 'Thực phẩm tươi', ƯU TIÊN gợi ý chuyến có 'Xe đông lạnh'.
  + Nếu KHÔNG có xe đông lạnh, hãy VẪN gợi ý các chuyến xe thường cùng tuyến (nếu có) kèm theo CẢNH BÁO BẢO QUẢN.
- CẢNH BÁO BẢO QUẢN: 'Lưu ý: Chuyến này không có xe đông lạnh, bạn cần tự chuẩn bị cách bảo quản (đá khô, thùng xốp) kỹ càng nhé. Tài xế chỉ nhận hàng và chở đi thôi ạ'.
- CẤM GỢI Ý RA TRẠM: Tuyệt đối KHÔNG bảo khách 'mang hàng ra trạm để tìm chuyến' hoặc 'mang ra trạm chờ'. Chỉ tư vấn chuyến có sẵn hoặc tạo đơn chờ.
- CẤU TRÚC PHẢN HỒI: Liệt kê các chuyến bằng gạch đầu dòng, in đậm Mã chuyến, Lộ trình, Thời gian. 

Nhiệm vụ 2: LUÔN HỎI CÂN NẶNG & KÍCH THƯỚC:
- Bạn CẦN BIẾT cân nặng (kg) và kích thước (Dài x Rộng x Cao cm) để tính giá chính xác theo khối lượng quy đổi (Max Weight vs Volume).
- Nếu khách chưa cung cấp, hãy hỏi: 'Bạn cho mình xin **cân nặng (kg)** và **kích thước (Dài x Rộng x Cao cm)** của gói hàng để mình tính giá chính xác nhất nhé!'.
- Nếu khách đã cung cấp, dùng các giá trị đó để tính giá và chèn vào link.

Nhiệm vụ 3: TÍNH GIÁ CƯỚC ƯỚC TÍNH:
- Công thức (NỘI BỘ): 
  + Thể tích (m3) = (Dài * Rộng * Cao) / 1,000,000
  + Khối lượng quy đổi = Thể tích * {vwf}
  + Chargeable Weight = Max(Khối lượng thực, Khối lượng quy đổi)
  + Giá ước tính = Max({minPrice}, (BasePrice * Chargeable Weight / Sức chứa xe) * Hệ số Tuyến * Hệ số Loại hàng).
- QUY TẮC HIỂN THỊ GIÁ: 
  + TUYỆT ĐỐI KHÔNG HIỂN THỊ CÔNG THỨC. CHỈ TRẢ VỀ KẾT QUẢ CUỐI CÙNG.
  + NẾU THIẾU THÔNG TIN (Khối lượng, Lộ trình, Kích thước...) hoặc KHÔNG CÓ CHUYẾN: KHÔNG tính giá, không ghi 'Giá ước tính = ...'.

Nhiệm vụ 4: HỖ TRỢ ĐẶT ĐƠN & DỊCH VỤ:
- ĐƠN HÀNG CHỜ: Nếu KHÔNG tìm thấy bất kỳ chuyến xe nào phù hợp, hãy nói: 'Hiện chưa có chuyến xe nào chạy tuyến này vào thời gian bạn yêu cầu. Tuy nhiên, mình có thể giúp bạn đăng một **đơn hàng chờ** lên hệ thống nhé!'. 
- LƯU Ý ĐĂNG NHẬP: Nếu khách chưa đăng nhập (không thấy lịch sử đơn hàng), hãy nhắc: 'Bạn vui lòng đăng nhập tài khoản để mình có thể hỗ trợ tạo đơn hàng này nhé!'. TUYỆT ĐỐI không cung cấp link CONFIRM_LINK cho ĐƠN HÀNG CHỜ nếu khách chưa đăng nhập.
- PHÍ LẤY HÀNG TẬN NƠI: Nếu khách chọn 'Tại nhà', nhắc: 'Việc lấy hàng tận nơi có thể phát sinh thêm chi phí nhỏ do tài xế báo trực tiếp cho bạn nhé'.
{gpsHint}
- QUY TẮC GIAO TIẾP: KHÔNG hiển thị ID hệ thống. Tuyệt đối KHÔNG DÙNG VIẾT HOA TOÀN BỘ (CAPSLOCK) khi trả lời khách (trừ các từ viết tắt hợp lệ).
- KẾT THÚC: Nếu đã đủ thông tin (bao gồm kích thước) và khách ĐÃ ĐĂNG NHẬP, hiển thị link: 
  CONFIRM_LINK[fromId=[ID_TÌNH_ĐI]&toId=[ID_TỈNH_ĐẾN]&weight=[KG]&l=[DÀI]&w=[RỘNG]&h=[CAO]&desc=[LOẠI_HÀNG]&phone=[SDT_NHẬN]&pType=[2_BẾN_1_NHÀ]&dType=[2_BẾN_1_NHÀ]&pAddr=[ĐC_ĐI]&dAddr=[ĐC_ĐẾN]&tripId=[MÃ_CHUYẾN]]
  *(Nếu là đơn ghép vào chuyến cụ thể thì mới cho phép guest nhấn để nhảy sang trang đăng nhập)*
";

                if (roleClaim != "1" && roleClaim != "3")
                {
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

                    if (nearestStation != null && minDistance < 100)
                    {
                        if (!combinedProvinceIds.Any())
                        {
                            combinedProvinceIds.Add(nearestStation.ProvinceId);
                        }
                        contextInfo += $"[HỆ THỐNG ĐÃ ĐỊNH VỊ GPS: Khách hàng đang đứng cách trạm '{nearestStation.StationName}' ({nearestStation.Province.ProvinceName}) khoảng {minDistance:F1} km. Hãy ưu tiên gợi ý các chuyến đi qua trạm này hoặc nhắc người dùng mang hàng ra trạm tiện nhất.]\n";
                    }

                    bool isTomorrow = userMessage.ToLower().Contains("ngày mai") || userMessage.ToLower().Contains("mai");

                    var tripQuery = _context.Trips
                        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.TripStations).ThenInclude(ts => ts.Station).ThenInclude(s => s.Province)
                        .Include(t => t.Vehicle).ThenInclude(v => v.VehicleType)
                        .Include(t => t.RouteTypeNavigation)
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

                    contextInfo += "\nHỆ THỐNG TRIP DATA:\n";

                    if (trips.Any()) {
                        foreach (var t in trips) {
                            var intermediateStops = string.Join(" -> ", t.TripStations.OrderBy(ts => ts.StopOrder).Select(ts => $"{ts.Station.StationName}({ts.Station.Province.ProvinceName})").Distinct());
                            var stopsText = string.IsNullOrEmpty(intermediateStops) ? "" : $" (Đi qua: {intermediateStops})";
                            
                            contextInfo += $"- Mã {t.TripId}: {t.FromStationNavigation.StationName} ({t.FromStationNavigation.Province.ProvinceName}) đi {t.ToStationNavigation.StationName} ({t.ToStationNavigation.Province.ProvinceName}){stopsText} | Khởi hành lúc {t.StartTime:HH:mm} ngày {t.StartTime:dd/MM} | Loại xe: {t.Vehicle?.VehicleType?.TypeName ?? "N/A"} | Sức chứa: {t.Vehicle?.CapacityKg ?? 1000}kg | BasePrice: {t.BasePrice} | Hệ số Tuyến: {t.RouteTypeNavigation?.Multiplier ?? 1}\n";
                        }
                    } else {
                        contextInfo += "[Hệ thống báo cáo: Không tìm thấy chuyến xe nào phù hợp với yêu cầu này trên CSDL]\n";
                    }
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
4. TUYỆT ĐỐI KHÔNG dùng mã HTML (như <a>). Hãy chỉ dùng các placeholder: [[ACTION_BUTTONS_ID]] hoặc CONFIRM_LINK[...] như đã hướng dẫn trong phần Vai trò.
5. PHẢI TRẢ LỜI Ở ĐỊNH DẠNG VĂN BẢN PHẲNG (PLAIN TEXT). Không dùng Markdown cho link.

Lịch sử trò chuyện:
{history}

Người dùng hỏi: {userMessage}
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