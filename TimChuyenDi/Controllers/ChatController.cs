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
using System.Text;
using System.Globalization;
using System.Text.Json;

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

        private ChatOrderSession GetSessionOrder()
        {
            var json = HttpContext.Session.GetString("ChatOrder");
            return string.IsNullOrEmpty(json) ? new ChatOrderSession() : JsonSerializer.Deserialize<ChatOrderSession>(json);
        }

        private void SaveSessionOrder(ChatOrderSession order)
        {
            HttpContext.Session.SetString("ChatOrder", JsonSerializer.Serialize(order));
        }

        private Station FindNearestStation(double lat, double lng, int? provinceId = null)
        {
            var query = _context.Stations.AsQueryable();
            if (provinceId.HasValue) query = query.Where(s => s.ProvinceId == provinceId.Value);

            var stations = query.ToList();
            return stations
                .Select(s => new { Station = s, Distance = CalculateDistance(lat, lng, (double)s.Latitude, (double)s.Longitude) })
                .OrderBy(x => x.Distance)
                .FirstOrDefault()?.Station;
        }

        private double CalculateDistance(double lat1, double lng1, double lat2, double lng2)
        {
            var d1 = lat1 * (Math.PI / 180.0);
            var num1 = lng1 * (Math.PI / 180.0);
            var d2 = lat2 * (Math.PI / 180.0);
            var num2 = lng2 * (Math.PI / 180.0) - num1;
            var d3 = Math.Pow(Math.Sin((d2 - d1) / 2.0), 2.0) + Math.Cos(d1) * Math.Cos(d2) * Math.Pow(Math.Sin(num2 / 2.0), 2.0);
            return 6371000.0 * (2.0 * Math.Atan2(Math.Sqrt(d3), Math.Sqrt(1.0 - d3))) / 1000.0;
        }

        private bool IsPositiveConfirm(string msg)
        {
            string norm = RemoveDiacritics(msg.ToLower());
            // Hỗ trợ viết tắt, sai chính tả, tiếng lóng
            string[] keys = { "ok", "dung", "chuan", "dr", "chuaanw", "u", "uh", "ya", "oi", "dong y", "dc", "duoc", "chot", "chinh xac" };
            return keys.Any(k => Regex.IsMatch(norm, @"\b" + k + @"\b") || norm.Contains(k));
        }

        private string GetStepInstruction(OrderStep step)
        {
            return step switch
            {
                OrderStep.AskRoute => "Nhiệm vụ: Xác định lộ trình. Cần: Tỉnh đi, Tỉnh đến, Địa chỉ chi tiết điểm đi/đến. (Nếu đã chọn TripId thì bước này coi như xong).",
                OrderStep.AskCargo => "Nhiệm vụ: Lấy thông tin hàng hóa. Cần: Tên loại hàng ( mô tả hàng hóa ), Cân nặng (kg), Kích thước (dài x rộng x cao).",
                OrderStep.AskReceiver => "Nhiệm vụ: Thông tin người nhận. Cần: Họ tên, Số điện thoại.",
                OrderStep.AskTime => "Nhiệm vụ: Thời gian gửi hàng. Cần: PickupTimeFrom(bắt buộc) và PickupTimeTo (nếu có).Nếu có khoảng thời gian → lưu cả From & To.Chỉ hỏi nếu thiếu PickupTimeFrom.",
                OrderStep.Confirm => "Nhiệm vụ: Xác nhận đơn. Hiển thị bảng tóm tắt thông tin và hỏi khách 'Chốt đơn chưa?'.",
                _ => ""
            };
        }

        private string GetMissingFieldsInfo(ChatOrderSession order)
        {
            var missing = new List<string>();
            switch (order.CurrentStep)
            {
                case OrderStep.AskRoute:
                    if (!order.FromProvinceId.HasValue) missing.Add("Tỉnh đi (FromProvinceId)");
                    if (!order.ToProvinceId.HasValue) missing.Add("Tỉnh đến (ToProvinceId)");
                    if (string.IsNullOrEmpty(order.PickupAddress)) missing.Add("Địa chỉ lấy hàng (PickupAddress)");
                    if (string.IsNullOrEmpty(order.DeliveryAddress)) missing.Add("Địa chỉ giao hàng (DeliveryAddress)");
                    break;
                case OrderStep.AskCargo:
                    if (order.Weight <= 0) missing.Add("Khối lượng (Weight)");
                    if (string.IsNullOrEmpty(order.Description)) missing.Add("Mô tả hàng hóa (Description)");
                    break;
                case OrderStep.AskReceiver:
                    if (string.IsNullOrEmpty(order.ReceiverName)) missing.Add("Tên người nhận (ReceiverName)");
                    if (string.IsNullOrEmpty(order.ReceiverPhone)) missing.Add("SĐT người nhận (ReceiverPhone)");
                    break;
                case OrderStep.AskTime:
                    if (!order.PickupTimeFrom.HasValue) missing.Add("Thời gian lấy hàng (PickupTimeFrom)");
                    break;
            }
            return missing.Any() ? "Các thông tin còn thiếu BẮT BUỘC: " + string.Join(", ", missing) : "Đã đủ thông tin.";
        }

        private void UpdateCurrentStep(ChatOrderSession order)
        {
            if (order.CurrentStep == OrderStep.None) order.CurrentStep = OrderStep.AskRoute;

            bool routeDone = (order.FromProvinceId.HasValue && order.ToProvinceId.HasValue && !string.IsNullOrEmpty(order.PickupAddress) && !string.IsNullOrEmpty(order.DeliveryAddress)) || order.TripId.HasValue;
            bool cargoDone = order.Weight > 0 && !string.IsNullOrEmpty(order.Description);
            bool receiverDone = !string.IsNullOrEmpty(order.ReceiverName) && !string.IsNullOrEmpty(order.ReceiverPhone);
            bool timeDone = order.PickupTimeFrom.HasValue;

            if (!routeDone) order.CurrentStep = OrderStep.AskRoute;
            else if (!cargoDone) order.CurrentStep = OrderStep.AskCargo;
            else if (!receiverDone) order.CurrentStep = OrderStep.AskReceiver;
            else if (!timeDone) order.CurrentStep = OrderStep.AskTime;
            else order.CurrentStep = OrderStep.Confirm;
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
                bool isFirstMessage = string.IsNullOrWhiteSpace(history);
                string contextInfo = "";
                string currentTime = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                var userDisplayName = User.FindFirstValue("FullName") ?? User.Identity.Name ?? "Quý khách";
                string roleName = roleClaim switch { "1" => "Quản trị viên", "3" => "Tài xế", _ => "Khách hàng" };

                // Get All Reference Data
                var allProvinces = await _context.Provinces.ToListAsync();
                var provincesWithNorm = allProvinces.Select(p => new { 
                    Province = p, 
                    NameNorm = RemoveDiacritics(NormalizeName(p.ProvinceName)) 
                }).ToList();

                // Normalize inputs
                string normUserMsg = RemoveDiacritics(NormalizeQuery(userMessage ?? ""));
                string normHistory = RemoveDiacritics(NormalizeQuery(history ?? ""));
                string normFullText = normHistory + " " + normUserMsg;

                // --- 📦 QUẢN LÝ ĐƠN HÀNG (SESSION) ---
                var order = GetSessionOrder();
                bool isOrderIntent = order.IsActive || Regex.IsMatch(normFullText, @"dat|tao|gui|ship|don|hang|muon gui");
                
                if (isOrderIntent && !order.IsActive) {
                    order.IsActive = true;
                    order.CurrentStep = OrderStep.AskRoute;
                    // Lấy SĐT mặc định từ Profile nếu đã đăng nhập
                    if (User.Identity.IsAuthenticated) {
                        var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId.ToString() == userIdClaim);
                        order.SenderPhone = currentUser?.Phone;
                    }
                }


                // ⚖️ XỬ LÝ CÂN NẶNG
                // Nếu khách cung cấp weight trực tiếp thì không cần confirmed weightSuggest
                if (order.WeightSuggest.HasValue && order.Weight == 0 && IsPositiveConfirm(userMessage)) {
                    order.Weight = order.WeightSuggest.Value;
                    order.WeightSuggest = null;
                }

                // 📦 XỬ LÝ CHỌN CHUYẾN XE (Ưu tiên ID -> Sau đó là xác nhận gợi ý)
                var tripMatch = Regex.Match(userMessage, @"(?i)(ma|chuyen|id|so|lay|ba|chon)\s*(\d{2,5})");
                bool isExplicitSelection = false;

                if (tripMatch.Success && int.TryParse(tripMatch.Groups[2].Value, out int selectedTripId))
                {
                    var selTrip = await _context.Trips
                        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .FirstOrDefaultAsync(t => t.TripId == selectedTripId);
                    
                    if (selTrip != null) {
                        order.TripId = selTrip.TripId;
                        order.CurrentStep = OrderStep.AskCargo;
                        order.FromProvinceId = selTrip.FromStationNavigation.ProvinceId;
                        order.ToProvinceId = selTrip.ToStationNavigation.ProvinceId;
                        order.FromStationId = selTrip.FromStation;
                        order.ToStationId = selTrip.ToStation;
                        if (!order.PickupTimeFrom.HasValue) order.PickupTimeFrom = selTrip.StartTime;
                        order.PickupAddress = selTrip.FromStationNavigation.StationName;
                        order.DeliveryAddress = selTrip.ToStationNavigation.StationName;
                        isExplicitSelection = true;
                    } else {
                        contextInfo += $"\nHỆ THỐNG: Khách hàng yêu cầu mã chuyến {selectedTripId} nhưng mã này không hợp lệ hoặc không tìm thấy.";
                    }
                }
                
                // Nếu không có ID cụ thể nhưng khách nói "Ok/Đúng rồi..."
                if (!isExplicitSelection && !order.TripId.HasValue && order.TripSuggestions?.Any() == true && IsPositiveConfirm(userMessage))
                {
                    // Thay vì tự chọn, ta thêm hint để AI hỏi xác nhận lại cho chắc (theo yêu cầu user)
                    int firstId = order.TripSuggestions.First();
                    contextInfo += $"\nHệ thống: Khách hàng có vẻ đang muốn chọn chuyến xe đầu tiên (Mã {firstId}). Bạn hãy hỏi xác nhận: 'Bạn muốn chọn luôn chuyến mã {firstId} đúng không?' để chắc chắn.";
                }

                bool isSelectingTrip = isExplicitSelection || tripMatch.Success;
                // 🛑 Kiểm tra auth trước khi tiếp tục
                if (isOrderIntent && !User.Identity.IsAuthenticated) {
                    string loginPrompt = "Khách hàng muốn đặt đơn nhưng CHƯA ĐĂNG NHẬP. Hãy lịch sự chào hỏi và hướng dẫn khách Đăng nhập hoặc Đăng ký tài khoản Gió Việt trước khi có thể tạo đơn hàng. Bạn có thể giải thích ngắn gọn lợi ích của việc đăng nhập để quản lý đơn hàng tốt hơn.";
                    string loginReply = await _openAIService.SendMessageAsync($"USER: {userMessage}\n\nINSTRUCTION: {loginPrompt}");
                    return Json(new { success = true, reply = loginReply });
                }

                // 1. Identify Provinces in CURRENT message (Priority for Direction)
                var matchedInMsg = provincesWithNorm
                    .Where(p => normUserMsg.Contains(p.NameNorm))
                    .Select(p => new { p.Province, Index = normUserMsg.IndexOf(p.NameNorm) })
                    .OrderBy(p => p.Index)
                    .ToList();

                Province fromProvince = null;
                Province toProvince = null;
                string toKeywords = @"(den|toi|di|ve|tram|ra|vao|len|xuong|sang)";
                string fromKeywords = @"(tu|o|tai|xuat phat)";

                if (matchedInMsg.Count >= 2) {
                    // Two or more provinces: usually From then To
                    var first = matchedInMsg[0];
                    var second = matchedInMsg[1];

                    // Check if second has a "to" keyword before it
                    var segmentBeforeSecond = normUserMsg.Substring(Math.Max(0, second.Index - 12), Math.Min(12, second.Index));
                    if (Regex.IsMatch(segmentBeforeSecond, toKeywords)) {
                        fromProvince = first.Province;
                        toProvince = second.Province;
                    } else {
                        // Check if first has a "from" keyword
                        var segmentBeforeFirst = normUserMsg.Substring(Math.Max(0, first.Index - 12), Math.Min(12, first.Index));
                        if (Regex.IsMatch(segmentBeforeFirst, fromKeywords)) {
                            fromProvince = first.Province;
                            toProvince = second.Province;
                        } else {
                            // Default: First is From, Second is To
                            fromProvince = first.Province;
                            toProvince = second.Province;
                        }
                    }
                } else if (matchedInMsg.Count == 1) {
                    // Only one province in current message: check direction
                    var m = matchedInMsg[0];
                    var segmentBefore = normUserMsg.Substring(Math.Max(0, m.Index - 12), Math.Min(12, m.Index));
                    if (Regex.IsMatch(segmentBefore, toKeywords)) toProvince = m.Province;
                    else fromProvince = m.Province;
                }

                // Cập nhật Province từ message CHỈ KHI chưa chọn Trip (để tránh override trip route)
                if (!order.TripId.HasValue)
                {
                    if (fromProvince != null) order.FromProvinceId = fromProvince.ProvinceId;
                    if (toProvince != null) order.ToProvinceId = toProvince.ProvinceId;

                    // 2. Fallback to History (If missing From or To)
                    if (order.FromProvinceId == null || order.ToProvinceId == null)
                    {
                        var matchedInFull = provincesWithNorm
                            .Where(p => normFullText.Contains(p.NameNorm))
                            .Select(p => p.Province)
                            .ToList();

                        if (order.FromProvinceId == null) order.FromProvinceId = matchedInFull.FirstOrDefault()?.ProvinceId;
                        if (order.ToProvinceId == null) order.ToProvinceId = matchedInFull.FirstOrDefault(p => p.ProvinceId != (order.FromProvinceId ?? -1))?.ProvinceId;
                    }
                }

                // Safety check: From and To MUST be different
                if (order.FromProvinceId != null && order.ToProvinceId != null && order.FromProvinceId == order.ToProvinceId) order.ToProvinceId = null;

                // 3. Time Range
                DateTime? rangeStart = null;
                DateTime? rangeEnd = null;
                if (Regex.IsMatch(normFullText, @"hom nay")) { rangeStart = DateTime.Now; rangeEnd = DateTime.Now.Date.AddDays(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normFullText, @"toi nay")) { rangeStart = DateTime.Now.Date.AddHours(18); if (rangeStart < DateTime.Now) rangeStart = DateTime.Now; rangeEnd = DateTime.Now.Date.AddDays(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normFullText, @"ngay mai")) { rangeStart = DateTime.Now.Date.AddDays(1); rangeEnd = rangeStart.Value.AddDays(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normFullText, @"ngay kia")) { rangeStart = DateTime.Now.Date.AddDays(2); rangeEnd = rangeStart.Value.AddDays(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normFullText, @"tuan nay")) { rangeStart = DateTime.Now; int diff = (7 - (int)DateTime.Now.DayOfWeek) % 7; rangeEnd = DateTime.Now.Date.AddDays(diff + 1).AddSeconds(-1); }
                else if (Regex.IsMatch(normFullText, @"tuan sau")) { 
                    int daysUntilMonday = ((int)DayOfWeek.Monday - (int)DateTime.Now.DayOfWeek + 7) % 7;
                    if (daysUntilMonday == 0) daysUntilMonday = 7;
                    rangeStart = DateTime.Now.Date.AddDays(daysUntilMonday); 
                    rangeEnd = rangeStart.Value.AddDays(7).AddSeconds(-1); 
                }
                else if (Regex.IsMatch(normFullText, @"thang nay")) { rangeStart = DateTime.Now; rangeEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normFullText, @"thang sau")) { 
                    rangeStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(1); 
                    rangeEnd = rangeStart.Value.AddMonths(1).AddSeconds(-1); 
                }
                else {
                    var dateYearMatch = Regex.Match(normFullText, @"ngay (\d{1,2})[/-](\d{1,2})[/-](\d{4})");
                    if (dateYearMatch.Success && int.TryParse(dateYearMatch.Groups[1].Value, out int d2) && int.TryParse(dateYearMatch.Groups[2].Value, out int m2) && int.TryParse(dateYearMatch.Groups[3].Value, out int y2)) {
                        try { rangeStart = new DateTime(y2, m2, d2); rangeEnd = rangeStart.Value.AddDays(1).AddSeconds(-1); } catch {}
                    } else {
                        var dateMatch = Regex.Match(normFullText, @"ngay (\d{1,2})[/-](\d{1,2})");
                        if (dateMatch.Success && int.TryParse(dateMatch.Groups[1].Value, out int d) && int.TryParse(dateMatch.Groups[2].Value, out int m)) {
                            try { rangeStart = new DateTime(DateTime.Now.Year, m, d); rangeEnd = rangeStart.Value.AddDays(1).AddSeconds(-1); } catch {}
                        }
                    }
                }

                // 4. Search & Filter trips (Chỉ thực hiện nếu CHƯA chọn TripId)
                int? fromId = order.FromProvinceId;
                int? toId = order.ToProvinceId;
                
                // Phát hiện thay đổi thông tin (đổi ý hoặc bổ sung thời gian)
                bool isChangeIntent = IsChangeIntent(userMessage);
                bool isTimeMentioned = Regex.IsMatch(normUserMsg, @"hom nay|toi nay|ngay mai|ngay kia|tuan nay|tuan sau|thang nay|thang sau|ngay \d");
                bool isTimeChanged = (rangeStart.HasValue || rangeEnd.HasValue) && isTimeMentioned;
                
                bool hasSuggestions = order.TripSuggestions?.Any() == true;
                bool isTripSearch = Regex.IsMatch(normFullText, @"chuyen|xe|lich|tim|di|den|gui|ship") || fromId.HasValue || toId.HasValue;
                bool showTripList = isFirstMessage || order.CurrentStep == OrderStep.None || order.CurrentStep == OrderStep.AskRoute || order.CurrentStep == OrderStep.Confirm;

                // Khóa tìm kiếm mềm: Chỉ tìm lại khi chưa có gợi ý, hoặc khách đổi ý, hoặc đổi thời gian
                bool needRefilter = !hasSuggestions || (!isSelectingTrip && (isChangeIntent || isTimeChanged));

                if (isChangeIntent || isTimeChanged) {
                    order.TripId = null;
                    order.TripSuggestions = null;
                    order.TripSuggestionsInfo = null;
                    hasSuggestions = false;
                    needRefilter = true;
                }

                if (!order.TripId.HasValue && needRefilter && (isTripSearch || showTripList)) {
                    var query = _context.Trips
                        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.TripStations).ThenInclude(ts => ts.Station).ThenInclude(s => s.Province)
                        .Where(t => new[] { 0, 1 }.Contains(t.Status));

                    if (rangeStart.HasValue && rangeEnd.HasValue) query = query.Where(t => t.StartTime >= rangeStart.Value && t.StartTime <= rangeEnd.Value);
                    else query = query.Where(t => t.StartTime > DateTime.Now.AddHours(-1));

                    var allTrips = await query.OrderBy(t => t.StartTime).ToListAsync();
                    var filteredTrips = allTrips;

                    if (fromId.HasValue && toId.HasValue) {
                        filteredTrips = allTrips.Where(t => {
                            var route = new List<int> { t.FromStationNavigation.ProvinceId };
                            route.AddRange(t.TripStations.OrderBy(ts => ts.StopOrder).Select(ts => ts.Station.ProvinceId));
                            route.Add(t.ToStationNavigation.ProvinceId);
                            int fIdx = route.IndexOf(fromId.Value);
                            int tIdx = route.LastIndexOf(toId.Value);
                            return fIdx != -1 && tIdx != -1 && fIdx < tIdx;
                        }).ToList();
                    } else if (fromId.HasValue) {
                        filteredTrips = allTrips.Where(t => t.FromStationNavigation.ProvinceId == fromId || t.TripStations.Any(ts => ts.Station.ProvinceId == fromId)).ToList();
                    } else if (toId.HasValue) {
                        filteredTrips = allTrips.Where(t => t.ToStationNavigation.ProvinceId == toId || t.TripStations.Any(ts => ts.Station.ProvinceId == toId)).ToList();
                    }

                    if (filteredTrips.Any()) {
                        order.TripSuggestions = filteredTrips.Select(t => t.TripId).Take(5).ToList();
                        order.TripSuggestionsInfo = filteredTrips.Take(10).Select(t =>
                            $"- Mã {t.TripId}: {t.FromStationNavigation.StationName} ({t.FromStationNavigation.Province.ProvinceName}) -> {t.ToStationNavigation.StationName} ({t.ToStationNavigation.Province.ProvinceName}) lúc {t.StartTime:HH:mm dd/MM}.").ToList();

                        if (showTripList) {
                            contextInfo += "\nDANH SÁCH CHUYẾN XE TÌM ĐƯỢC:\n" + string.Join("\n", order.TripSuggestionsInfo);
                        }
                    } else if (needRefilter) {
                        contextInfo += "\nHỆ THỐNG: Không tìm thấy chuyến xe nào phù hợp trong dữ liệu hệ thống.";
                        order.TripSuggestions = null; 
                        order.TripSuggestionsInfo = null;
                    }
                }

                // 4.1. Gửi gợi ý từ Session vào Prompt CHỈ KHI ở step chọn tuyến/chuyến (Hoặc khi đang cố gắng chọn)
                bool isSelectionStep = order.CurrentStep == OrderStep.AskRoute || order.CurrentStep == OrderStep.None;
                bool userIsPicking = Regex.IsMatch(normUserMsg, @"ma|chuyen|id|so|lay|ok|dung|chuan|chot|chon");
                
                // Hiển thị list nếu: (Chưa chọn OR Đang chọn/đổi) AND (Đang ở bước chọn OR Đang chọn/đổi)
                bool showSuggestions = (!order.TripId.HasValue || userIsPicking) && (isSelectionStep || userIsPicking);
                
                if (showSuggestions && order.TripSuggestionsInfo?.Any() == true && !contextInfo.Contains("DANH SÁCH CHUYẾN XE"))
                {
                    contextInfo += "\nDANH SÁCH CHUYẾN XE GỢI Ý (TỪ LỊCH SỬ TÌM KIẾM):\n" + string.Join("\n", order.TripSuggestionsInfo);
                }

                // 4.2. Cập nhật tọa độ từ Client (nếu có) vào Session
                if (lat.HasValue && lng.HasValue) {
                    var nearSt = FindNearestStation(lat.Value, lng.Value, fromId);
                    if (nearSt != null && !order.FromStationId.HasValue) {
                        order.FromStationId = nearSt.StationId;
                        order.PickupAddress = nearSt.StationName;
                    }
                }

                // 5. Nếu ĐÃ chọn TripId, hiển thị thông tin chi tiết vào context và đồng bộ địa chỉ
                if (order.TripId.HasValue)
                {
                    var selectedTrip = await _context.Trips
                        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .FirstOrDefaultAsync(t => t.TripId == order.TripId.Value);
                    
                    if (selectedTrip != null) {
                        contextInfo += $"\nTHÔNG TIN CHUYẾN XE ĐÃ CHỌN (Mã {selectedTrip.TripId}):\n" +
                                       $"- Tuyến: {selectedTrip.FromStationNavigation.Province.ProvinceName} -> {selectedTrip.ToStationNavigation.Province.ProvinceName}\n" +
                                       $"- Điểm đi: {selectedTrip.FromStationNavigation.StationName} ({selectedTrip.FromStationNavigation.Address})\n" +
                                       $"- Điểm đến: {selectedTrip.ToStationNavigation.StationName} ({selectedTrip.ToStationNavigation.Address})\n" +
                                       $"- Thời gian khởi hành: {selectedTrip.StartTime:HH:mm dd/MM/yyyy}\n";
                        
                        // Đồng bộ lại tỉnh đi/đến từ Trip đã chọn (Khóa cứng route)
                        order.FromProvinceId = selectedTrip.FromStationNavigation.ProvinceId;
                        order.ToProvinceId = selectedTrip.ToStationNavigation.ProvinceId;

                        // AI có thể dùng thông tin này để tự động cập nhật OrderSession thông qua thẻ [[UPDATE_ORDER]]
                        if (string.IsNullOrEmpty(order.PickupAddress)) order.PickupAddress = selectedTrip.FromStationNavigation.StationName;
                        if (string.IsNullOrEmpty(order.DeliveryAddress)) order.DeliveryAddress = selectedTrip.ToStationNavigation.StationName;
                    }
                }

                // AI Instruction (Very Explicit)
                string aiInstruction = $"Bạn là Trợ Gió - Trợ lý vận chuyển Gió Việt. Mục tiêu: Hỗ trợ tìm chuyến xe và tạo đơn hàng. " +
                                       "CẤM lặp lại lời chào hoặc giới thiệu tên nhiều lần. Trả lời CỰC KỲ ngắn gọn, đi thẳng vào vấn đề. " +
                                       "Chỉ liệt kê thông tin trong context. Không bịa đặt. " +
                                       "Lưu ý: Chỉ nhận gửi hàng, KHÔNG chở người. " +
                                       "KHI LIỆT KÊ CHUYẾN XE: Sau mỗi chuyến xe tìm được TRONG CONTEXT, bạn BẮT BUỘC phải chèn thẻ [[ACTION_BUTTONS_Id]] ngay sau mã chuyến. " +
                                       "Nếu khách chọn chuyến mà context không có mã đó, tuyệt đối không tự ý chọn mã khác thay thế, hãy báo lại mã này không tìm thấy.";

                if (order.IsActive) {
                    UpdateCurrentStep(order);
                    
                    aiInstruction += "\n--- 📦 QUY TRÌNH TẠO ĐƠN (STATE MACHINE) ---" +
                                     $"\nBƯỚC HIỆN TẠI: {order.CurrentStep}" +
                                     $"\nNHIỆM VỤ CỦA BẠN: {GetStepInstruction(order.CurrentStep)}" +
                                     $"\nCHI TIẾT: {GetMissingFieldsInfo(order)}" +
                                     "\nTRẠNG THÁI ĐƠN HIỆN TẠI (JSON): " + JsonSerializer.Serialize(order) +
                                     "\nHƯỚNG DẪN CŨNG CỐ:" +
                                     "\n- Ưu tiên hỏi thông tin còn thiếu BẮT BUỘC." +
                                     "\n- Trích xuất thông tin từ tin nhắn và cập nhật vào JSON ngay. CẤM hỏi lại thông tin khách đã cung cấp." +
                                     "\n- CẤM nói dài dòng, CẤM chào hỏi lại khi đang trong quá trình đặt đơn." +
                                     "\n- Ở bước Confirm: Hiển thị tóm tắt ngắn gọn và hỏi chốt đơn.";
                    
                    aiInstruction += "\nQUAN TRỌNG: Mọi phản hồi khi đang đặt đơn PHẢI đi kèm thẻ: [[UPDATE_ORDER:{\"...\"}]]";
                }

                // Gợi ý định vị nếu chưa bật
                if (!lat.HasValue && roleClaim != "1")
                {
                    bool isActionIntent = isTripSearch || Regex.IsMatch(normFullText, @"tao|dang|ky|them|chuyen|don|hang");
                    if (isActionIntent)
                    {
                        aiInstruction += " QUAN TRỌNG: Hãy gợi ý người dùng bấm nút 'Bật định vị' (biểu tượng [[GEO_ICON]]) để chatbot lấy tọa độ và tìm kiếm trạm xe/chuyến xe chính xác nhất xung quanh họ.";
                    }
                }

                string aiReply = await _openAIService.SendMessageAsync($"CONTEXT:\n{contextInfo}\n\nUSER MESSAGE: {userMessage}\n\nINSTRUCTION: {aiInstruction}");

                // --- 🔄 XỬ LÝ CẬP NHẬT ĐƠN HÀNG TỪ AI ---
                var updateMatch = Regex.Match(aiReply, @"\[\[UPDATE_ORDER:\s*(\{.*?\})\]\]", RegexOptions.Singleline);
                if (updateMatch.Success)
                {
                    try {
                        var jsonUpdate = updateMatch.Groups[1].Value;
                        using var doc = JsonDocument.Parse(jsonUpdate);
                        var root = doc.RootElement;
                        
                        // Cập nhật các trường có trong JSON
                        if (root.TryGetProperty("Weight", out var w)) order.Weight = w.GetDecimal();
                        if (root.TryGetProperty("WeightSuggest", out var ws)) order.WeightSuggest = ws.GetDecimal();
                        if (root.TryGetProperty("Length", out var l)) order.Length = l.GetDecimal();
                        if (root.TryGetProperty("Width", out var wd)) order.Width = wd.GetDecimal();
                        if (root.TryGetProperty("Height", out var h)) order.Height = h.GetDecimal();
                        if (root.TryGetProperty("Description", out var desc)) order.Description = desc.GetString();
                        if (root.TryGetProperty("ReceiverName", out var rn)) order.ReceiverName = rn.GetString();
                        if (root.TryGetProperty("ReceiverPhone", out var rp)) order.ReceiverPhone = rp.GetString();
                        if (root.TryGetProperty("Note", out var n)) order.Note = n.GetString();
                        if (root.TryGetProperty("PickupAddress", out var pa)) order.PickupAddress = pa.GetString();
                        if (root.TryGetProperty("DeliveryAddress", out var da)) order.DeliveryAddress = da.GetString();
                        if (root.TryGetProperty("PickupType", out var pt)) order.PickupType = pt.GetInt32();
                        if (root.TryGetProperty("DeliveryType", out var dt)) order.DeliveryType = dt.GetInt32();

                        // Cập nhật lại Step ngay sau khi có data mới
                        UpdateCurrentStep(order);
                        
                        // XỬ LÝ XÁC NHẬN CUỐI CÙNG
                        if (order.CurrentStep == OrderStep.Confirm && Regex.IsMatch(normUserMsg, @"dung|ok|chot|xac nhan|dong y|chuan|chuaan")) {
                            aiReply += "\n\n[[CONFIRM_ORDER_ACTION]]";
                        }
                    } catch {}
                }

                // Lưu lại Session

                if (fromId.HasValue) order.FromProvinceId = fromId;
                if (toId.HasValue) order.ToProvinceId = toId;
                SaveSessionOrder(order);

                if (int.TryParse(userIdClaim, out int lUid)) _ = Task.Run(() => _behaviorService.ExtractAndLogBehaviorAsync(lUid, userMessage));

                return Json(new { success = true, reply = aiReply });
            }
            catch (Exception ex) {
                return Json(new { success = false, reply = "Err: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmOrder()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return Json(new { success = false, message = "Vui lòng đăng nhập." });
            int customerId = int.Parse(userIdStr);

            var order = GetSessionOrder();
            if (!order.IsActive) return Json(new { success = false, message = "Không tìm thấy đơn hàng tạm." });

            try
            {
                // 1. Tìm chuyến xe (nếu có)
                Trip trip = null;
                if (order.TripId.HasValue)
                {
                    trip = await _context.Trips
                        .Include(t => t.Vehicle)
                        .Include(t => t.RouteTypeNavigation)
                        .Include(t => t.FromStationNavigation)
                        .Include(t => t.ToStationNavigation)
                        .FirstOrDefaultAsync(t => t.TripId == order.TripId.Value);
                }

                // 2. Tính toán giá
                decimal totalPrice = 0;
                var vwFactorConfig = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.KeyName == "VolumeToWeightFactor");
                decimal vwFactor = vwFactorConfig?.Value ?? 250;
                var minPriceConfig = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.KeyName == "MinPrice");
                decimal minPrice = minPriceConfig?.Value ?? 0;

                decimal volume = (order.Length * order.Width * order.Height) / 1000000m;
                decimal chargeableWeight = Math.Max(order.Weight, volume * vwFactor);

                if (trip != null)
                {
                    decimal capacityKg = trip.Vehicle?.CapacityKg ?? 1;
                    decimal basePrice = trip.BasePrice * (chargeableWeight / capacityKg);
                    decimal tripTypeMultiplier = trip.RouteTypeNavigation?.Multiplier ?? 1;
                    totalPrice = Math.Max(basePrice * tripTypeMultiplier, minPrice);
                }
                else
                {
                    // Đơn chờ: Tính giá cơ bản dựa trên khoảng cách (nếu có From/To Province)
                    totalPrice = minPrice; // Hoặc một logic tính giá đơn chờ mặc định
                }

                // 3. Tạo ShipRequest
                var request = new Shiprequest
                {
                    UserId = customerId,
                    TripId = order.TripId,
                    Status = 0,
                    Note = order.Note ?? "Đơn hàng tạo từ Trợ lý Trợ Gió",
                    PickupTimeFrom = order.PickupTimeFrom ?? DateTime.Now,
                    PickupTimeTo = order.PickupTimeTo ?? (order.PickupTimeFrom?.AddDays(3) ?? DateTime.Now.AddDays(3)),
                    TotalPrice = totalPrice,
                    OrderCode = "TC" + DateTime.Now.Ticks.ToString().Substring(10),
                    CreatedAt = DateTime.Now
                };

                _context.Shiprequests.Add(request);
                await _context.SaveChangesAsync();
                request.OrderCode = "TC" + request.Id;

                // 4. Lưu Hàng hóa
                var cargo = new Cargodetail
                {
                    RequestId = request.Id,
                    Weight = order.Weight,
                    Length = order.Length,
                    Width = order.Width,
                    Height = order.Height,
                    Description = order.Description ?? "Hàng hóa từ Chatbot"
                };
                _context.Cargodetails.Add(cargo);

                // 5. Tạo Shipping Route
                var route = new Shippingroute
                {
                    RequestId = request.Id,
                    FromProvinceId = order.FromProvinceId,
                    ToProvinceId = order.ToProvinceId,
                    PickupType = order.PickupType,
                    DeliveryType = order.DeliveryType,
                    PickupAddress = order.PickupAddress,
                    DeliveryAddress = order.DeliveryAddress,
                    FromStationId = order.FromStationId,
                    ToStationId = order.ToStationId,
                    SenderPhone = order.SenderPhone,
                    ReceiverName = order.ReceiverName,
                    ReceiverPhone = order.ReceiverPhone
                };
                _context.Shippingroutes.Add(route);

                await _context.SaveChangesAsync();

                // Dọn dẹp Session
                HttpContext.Session.Remove("ChatOrder");

                return Json(new { success = true, requestId = request.Id, message = "Tạo đơn hàng thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi khi lưu đơn: " + ex.Message });
            }

        }

        [HttpGet]
        public async Task<IActionResult> GetStationsList(int provinceId)
        {
            var stations = await _context.Stations
                .Where(s => s.ProvinceId == provinceId)
                .Select(s => new { s.StationName, s.Address })
                .ToListAsync();
            return Json(stations);
        }

        [HttpPost]
        public IActionResult ResetOrder()
        {
            HttpContext.Session.Remove("ChatOrder");
            return Json(new { success = true });
        }

        private bool IsChangeIntent(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return false;
            string norm = RemoveDiacritics(msg.ToLower());
            string[] keywords = { "doi", "nham", "khong", "sua", "lai", "chuyen khac", "tim lai", "thay doi" };
            return keywords.Any(k => norm.Contains(k));
        }

        private string RemoveDiacritics(string text) {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalizedString) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLower().Replace("đ", "d");
        }

        private string NormalizeName(string name) {
            if (string.IsNullOrWhiteSpace(name)) return name;
            return name.ToLower().Replace("thành phố", "").Replace("tỉnh", "").Replace("tp.", "").Replace("t.", "").Replace("trạm", "").Replace("bến xe", "").Trim();
        }

        private string NormalizeQuery(string text) {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var res = text.ToLower();
            res = Regex.Replace(res, @"\bhn\b", "hà nội");
            res = Regex.Replace(res, @"\bđn\b|dn\b", "đà nẵng");
            res = Regex.Replace(res, @"\bhcm\b", "hồ chí minh");
            return res;
        }
    }
}