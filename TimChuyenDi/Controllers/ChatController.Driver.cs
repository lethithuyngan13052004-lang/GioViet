using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TimChuyenDi.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace TimChuyenDi.Controllers
{
    public partial class ChatController
    {
        private ChatTripSession GetSessionTrip()
        {
            var json = HttpContext.Session.GetString("ChatTrip");
            return string.IsNullOrEmpty(json) ? new ChatTripSession() : JsonSerializer.Deserialize<ChatTripSession>(json);
        }

        private void SaveSessionTrip(ChatTripSession session)
        {
            HttpContext.Session.SetString("ChatTrip", JsonSerializer.Serialize(session));
        }

        private async Task<IActionResult> HandleDriverMessage(string userMessage, string history, double? lat, double? lng, string userIdClaim, string userDisplayName, bool isFormPage = false)
        {
            try
            {
                int driverId = int.Parse(userIdClaim);
                var session = GetSessionTrip();
                string currentTime = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                string normUserMsg = RemoveDiacritics(NormalizeQuery(userMessage ?? ""));

                // Logic nhận diện hủy tác vụ
                if (Regex.IsMatch(normUserMsg, @"\b(huy|thoat|khong tao|bo qua|reset)\b") && session.IsActive)
                {
                    session = new ChatTripSession();
                    SaveSessionTrip(session);
                    return Json(new { success = true, reply = "HỆ THỐNG: Đã hủy bỏ tiến trình tạo chuyến xe. Bạn cần hỗ trợ gì khác không?" });
                }

                // AI System Prompt for Driver
                string systemPrompt = $@"Bạn là 'Trợ Gió', AI hỗ trợ TÀI XẾ cho ứng dụng Tìm Chuyến Đi.
Nhiệm vụ: Phân tích tin nhắn của tài xế và trả về JSON.
Thời gian hiện tại: {currentTime}.

Danh sách các Intent (Ý định) hợp lệ:
- CREATE_TRIP: Đăng chuyến xe mới.
- SEARCH_AVAILABLE_ORDERS: Tìm đơn hàng (khách đang chờ để ghép chuyến).
- MANAGE_PENDING_ORDERS: Xem các đơn chờ TÀI XẾ duyệt (đã gán TripId nhưng Status = 0).
- ACCEPT_ORDER: Nhận đơn hàng (yêu cầu TargetId).
- REJECT_ORDER: Từ chối đơn hàng (yêu cầu TargetId).
- SUMMARIZE_REVIEWS: Tổng hợp đánh giá của khách hàng.
- GENERAL: Trò chuyện bình thường.
- LƯU Ý QUAN TRỌNG 1: Tuyệt đối KHÔNG trả lời các câu hỏi KHÔNG LIÊN QUAN đến dịch vụ của ứng dụng (ví dụ: thời tiết, tin tức, lịch sử...). Nếu bị hỏi linh tinh, hãy xin lỗi và từ chối, báo rằng bạn chỉ hỗ trợ thông tin vận chuyển trên website.
- LƯU Ý QUAN TRỌNG 2: TUYỆT ĐỐI KHÔNG dùng tiếng Anh trong câu trả lời (Reply). Web này thuần Việt, không dùng các từ như form, stations, Trip, order... khi giao tiếp với người dùng. Hãy dùng 'trang tạo chuyến', 'trạm', 'chuyến xe', 'đơn hàng'.
- LƯU Ý QUAN TRỌNG 3: Nếu bạn đang hướng dẫn tài xế chọn Lộ trình (AskRoute) trên 'trang tạo chuyến', hãy nhắc tài xế 'nhấn vào ô Điểm xuất phát / Điểm đến để xác nhận trạm'. Để thu hút sự chú ý của họ vào ô Điểm xuất phát, BẮT BUỘC xuất thêm thẻ [[FOCUS_PROVINCE_START]] ở cuối câu Reply.

Thông tin hiện tại về tiến trình CREATE_TRIP (nếu có):
- Bước hiện tại: {session.CurrentStep}
- Trạm đi (Start): {(session.FromStationId.HasValue ? "Đã có" : "Chưa có")}
- Trạm đến (End): {(session.ToStationId.HasValue ? "Đã có" : "Chưa có")}
- Trạm phụ (Intermediates): {session.IntermediateStationIds.Count} trạm
- BasePrice: {session.BasePrice}
- StartTime: {session.StartTime}

Bạn PHẢI trả về ĐÚNG MỘT JSON hợp lệ (KHÔNG BỌC TRONG MARKDOWN BACKTICKS):
{{
  ""Intent"": ""CREATE_TRIP|SEARCH_AVAILABLE_ORDERS|MANAGE_PENDING_ORDERS|ACCEPT_ORDER|REJECT_ORDER|SUMMARIZE_REVIEWS|GENERAL"",
  ""TargetId"": 0, // Thay thế bằng ID số nguyên nếu người dùng muốn nhận/từ chối 1 đơn cụ thể
  ""Reply"": ""Câu trả lời tự nhiên của bạn (Nếu CREATE_TRIP, hãy dùng trường này để hướng dẫn tài xế cung cấp các thông tin còn thiếu. Ví dụ: 'Bạn muốn đi từ đâu?', 'Vui lòng cung cấp tải trọng trống', 'Bạn có muốn đi qua trạm trung gian nào không?')"",
  ""ExtractedTripData"": {{
    ""RouteType"": 0, // 1 (Bao nguyên chuyến), 2 (Ghép chuyến). Mặc định là 0 nếu không nhắc đến.
    ""StartStationQuery"": """", // Tên tỉnh/quận hoặc 'gần đây'
    ""EndStationQuery"": """", // Tên tỉnh/quận
    ""IntermediateStationQueries"": [], // Mảng các tên tỉnh/quận đi ngang qua
    ""StartTimeStr"": """", // Ví dụ: '15/12/2023 10:00'
    ""AvaiCapacityKg"": 0,
    ""BasePrice"": 0
  }}
}}

Lịch sử trò chuyện:
{history}
Tin nhắn của tài xế: {userMessage}";

                string intentJsonRaw = await _openAIService.SendMessageAsync(systemPrompt);
                
                intentJsonRaw = intentJsonRaw.Trim();
                if (intentJsonRaw.StartsWith("```json")) intentJsonRaw = intentJsonRaw.Substring(7);
                else if (intentJsonRaw.StartsWith("```")) intentJsonRaw = intentJsonRaw.Substring(3);
                if (intentJsonRaw.EndsWith("```")) intentJsonRaw = intentJsonRaw.Substring(0, intentJsonRaw.Length - 3);
                intentJsonRaw = intentJsonRaw.Trim();

                DriverIntentResponse parsedIntent = null;
                try
                {
                    parsedIntent = JsonSerializer.Deserialize<DriverIntentResponse>(intentJsonRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"JSON Parse Error: {ex.Message} - RAW: {intentJsonRaw}");
                    return Json(new { success = false, reply = "Xin lỗi, tôi không hiểu rõ ý của bạn. Vui lòng nói lại." });
                }

                if (parsedIntent == null) return Json(new { success = false, reply = "Lỗi phản hồi từ AI." });

                string replyText = parsedIntent.Reply;

                // 1. SUMMARIZE REVIEWS
                if (parsedIntent.Intent == "SUMMARIZE_REVIEWS")
                {
                    var reviews = await _context.Ratings
                        .Include(r => r.Req).ThenInclude(req => req.Trip)
                        .Where(r => r.Req.Trip.DriverId == driverId && r.CreatedAt >= DateTime.Now.AddDays(-30))
                        .OrderByDescending(r => r.RatingId)
                        .Take(20)
                        .ToListAsync();

                    if (reviews.Count < 10)
                    {
                        reviews = await _context.Ratings
                            .Include(r => r.Req).ThenInclude(req => req.Trip)
                            .Where(r => r.Req.Trip.DriverId == driverId)
                            .OrderByDescending(r => r.RatingId)
                            .Take(20)
                            .ToListAsync();
                    }

                    if (!reviews.Any())
                    {
                        return Json(new { success = true, reply = "Bạn chưa có đánh giá nào từ khách hàng trong hệ thống. Hãy chạy nhiều chuyến hơn nhé!" });
                    }

                    string reviewsData = string.Join("\n", reviews.Select(r => $"- {r.Score} sao: {r.Comment}"));
                    string summary = await _openAIService.SummarizeDriverReviewsAsync(reviewsData, userDisplayName);
                    
                    return Json(new { success = true, reply = summary });
                }

                // 2. MANAGE PENDING ORDERS
                if (parsedIntent.Intent == "MANAGE_PENDING_ORDERS")
                {
                    var requests = await _context.Shiprequests
                        .Include(r => r.User)
                        .Include(r => r.Cargodetails)
                        .Include(r => r.Shippingroutes).ThenInclude(sr => sr.FromStation).ThenInclude(s => s.Province)
                        .Include(r => r.Shippingroutes).ThenInclude(sr => sr.ToStation).ThenInclude(s => s.Province)
                        .Include(r => r.Trip).ThenInclude(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                        .Include(r => r.Trip).ThenInclude(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                        .Where(r => r.Trip.DriverId == driverId && r.Status == 0) // Chờ duyệt
                        .OrderByDescending(r => r.PickupTimeFrom)
                        .Take(10)
                        .ToListAsync();

                    if (!requests.Any())
                    {
                        return Json(new { success = true, reply = "Tuyệt vời, hiện tại không có đơn hàng nào đang chờ bạn phải xác nhận." });
                    }

                    string jsonPayload = JsonSerializer.Serialize(requests.Select(r => new {
                        Id = r.Id,
                        OrderCode = r.OrderCode,
                        PickupTime = r.PickupTimeFrom.ToString("dd/MM/yyyy HH:mm"),
                        CustomerName = r.User?.Name,
                        CargoDesc = r.Cargodetails.FirstOrDefault()?.Description,
                        Weight = r.Cargodetails.FirstOrDefault()?.Weight,
                        Price = r.TotalPrice
                    }));
                    
                    return Json(new { success = true, reply = $"Bạn có {requests.Count} đơn hàng đang chờ xác nhận.\n\n[[DRIVER_PENDING_LIST:{Uri.EscapeDataString(jsonPayload)}]]" });
                }

                // 3. ACCEPT / REJECT ORDER
                if ((parsedIntent.Intent == "ACCEPT_ORDER" || parsedIntent.Intent == "REJECT_ORDER") && parsedIntent.TargetId > 0)
                {
                    var req = await _context.Shiprequests.FirstOrDefaultAsync(r => r.Id == parsedIntent.TargetId);
                    if (req != null)
                    {
                        if (req.TripId == null && parsedIntent.Intent == "ACCEPT_ORDER")
                        {
                            var activeTrips = await _context.Trips
                                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                                .Where(t => t.DriverId == driverId && t.Status != 2 && t.StartTime >= DateTime.Today)
                                .OrderBy(t => t.StartTime)
                                .ToListAsync();

                            var activeTripsDto = activeTrips.Select(t => new {
                                Id = t.TripId,
                                Name = $"[Mã: {t.TripId}] {t.FromStationNavigation.Province.ProvinceName} ➡️ {t.ToStationNavigation.Province.ProvinceName} (Khởi hành: {t.StartTime:dd/MM/yyyy HH:mm})"
                            }).ToList();

                            string jsonPayloadMerge = JsonSerializer.Serialize(new { ReqId = parsedIntent.TargetId, ActiveTrips = activeTripsDto });
                            string confirmMerge = $"Đơn hàng **{req.OrderCode}** đang chờ ghép chuyến. Bạn muốn xử lý như thế nào?\n\n[[DRIVER_MERGE_OPTIONS:{Uri.EscapeDataString(jsonPayloadMerge)}]]";
                            return Json(new { success = true, reply = confirmMerge });
                        }

                        string actionName = parsedIntent.Intent == "ACCEPT_ORDER" ? "NHẬN" : "TỪ CHỐI";
                        string actionCode = parsedIntent.Intent == "ACCEPT_ORDER" ? "1" : "2";
                        string jsonPayload = JsonSerializer.Serialize(new { ReqId = parsedIntent.TargetId, Action = actionCode });
                        string confirmText = $"Bạn có chắc chắn muốn **{actionName}** đơn hàng **{req.OrderCode}** không?\n\n[[DRIVER_CONFIRM_ACTION:{Uri.EscapeDataString(jsonPayload)}]]";
                        return Json(new { success = true, reply = confirmText });
                    }
                    else
                    {
                        return Json(new { success = true, reply = $"Không tìm thấy đơn hàng với ID {parsedIntent.TargetId}." });
                    }
                }

                // 4. SEARCH AVAILABLE ORDERS
                if (parsedIntent.Intent == "SEARCH_AVAILABLE_ORDERS")
                {
                    // Truy vấn Shiprequest chờ ghép (TripId == null, Status == 0)
                    var query = _context.Shiprequests
                        .Include(r => r.User)
                        .Include(r => r.Cargodetails)
                        .Include(r => r.Shippingroutes).ThenInclude(sr => sr.FromStation).ThenInclude(s => s.Province)
                        .Include(r => r.Shippingroutes).ThenInclude(sr => sr.ToStation).ThenInclude(s => s.Province)
                        .Where(r => r.TripId == null && (r.Status == 0 || r.Status == null) && r.PickupTimeTo >= DateTime.Now);

                    var allAvailableQuery = query.AsEnumerable();

                    if (parsedIntent.ExtractedTripData != null)
                    {
                        if (!string.IsNullOrEmpty(parsedIntent.ExtractedTripData.StartStationQuery))
                        {
                            string q = RemoveDiacritics(parsedIntent.ExtractedTripData.StartStationQuery.ToLower());
                            allAvailableQuery = allAvailableQuery.Where(r => r.Shippingroutes.Any(sr => sr.FromStation != null && sr.FromStation.Province != null &&
                                (RemoveDiacritics(sr.FromStation.Province.ProvinceName.ToLower()).Contains(q) || RemoveDiacritics(sr.FromStation.StationName.ToLower()).Contains(q))));
                        }
                        if (!string.IsNullOrEmpty(parsedIntent.ExtractedTripData.EndStationQuery))
                        {
                            string q = RemoveDiacritics(parsedIntent.ExtractedTripData.EndStationQuery.ToLower());
                            allAvailableQuery = allAvailableQuery.Where(r => r.Shippingroutes.Any(sr => sr.ToStation != null && sr.ToStation.Province != null &&
                                (RemoveDiacritics(sr.ToStation.Province.ProvinceName.ToLower()).Contains(q) || RemoveDiacritics(sr.ToStation.StationName.ToLower()).Contains(q))));
                        }
                    }

                    var allAvailable = allAvailableQuery.OrderBy(r => r.PickupTimeTo).Take(20).ToList();

                    if (!allAvailable.Any())
                    {
                        return Json(new { success = true, reply = "Hiện tại không có đơn hàng nào chờ ghép chuyến." });
                    }

                    string jsonPayload = JsonSerializer.Serialize(allAvailable.Select(r => new {
                        Id = r.Id,
                        OrderCode = r.OrderCode,
                        PickupTime = r.PickupTimeFrom.ToString("dd/MM/yyyy HH:mm"),
                        CustomerName = r.User?.Name,
                        From = r.Shippingroutes.FirstOrDefault()?.FromStation?.Province?.ProvinceName ?? r.Shippingroutes.FirstOrDefault()?.PickupAddress,
                        To = r.Shippingroutes.FirstOrDefault()?.ToStation?.Province?.ProvinceName ?? r.Shippingroutes.FirstOrDefault()?.DeliveryAddress,
                        CargoDesc = r.Cargodetails.FirstOrDefault()?.Description,
                        Weight = r.Cargodetails.FirstOrDefault()?.Weight,
                        Price = r.TotalPrice
                    }));

                    return Json(new { success = true, reply = $"Tìm thấy {allAvailable.Count} đơn hàng chờ ghép chuyến.\n\n[[DRIVER_AVAILABLE_LIST:{Uri.EscapeDataString(jsonPayload)}]]" });
                }

                // 5. CREATE TRIP FLOW
                if (parsedIntent.Intent == "CREATE_TRIP" || session.IsActive)
                {
                    bool isNewSession = !session.IsActive;
                    session.IsActive = true;
                    if (session.CurrentStep == TripStep.None) session.CurrentStep = TripStep.AskVehicleAndType;

                    var data = parsedIntent.ExtractedTripData;

                    // Extract Data
                    if (data != null)
                    {
                        if (data.RouteType == 1 || data.RouteType == 2) session.RouteType = data.RouteType;
                        if (!string.IsNullOrEmpty(data.StartStationQuery)) session.FromStationQuery = data.StartStationQuery;
                        if (!string.IsNullOrEmpty(data.EndStationQuery)) session.ToStationQuery = data.EndStationQuery;
                        if (data.IntermediateStationQueries != null && data.IntermediateStationQueries.Any())
                        {
                            foreach(var query in data.IntermediateStationQueries)
                            {
                                if (!session.IntermediateQueries.Contains(query)) session.IntermediateQueries.Add(query);
                            }
                        }

                        if (data.AvaiCapacityKg > 0) session.AvaiCapacityKg = data.AvaiCapacityKg;
                        if (data.BasePrice > 0) session.BasePrice = data.BasePrice;

                        if (!string.IsNullOrEmpty(data.StartTimeStr))
                        {
                            if (DateTime.TryParse(data.StartTimeStr, out DateTime dt)) session.StartTime = dt;
                            else session.StartTime = DateTime.Now; // fallback
                        }
                    }

                    if (isNewSession && !isFormPage)
                    {
                        // Match stations
                        if (!string.IsNullOrEmpty(session.FromStationQuery) && !session.FromStationId.HasValue)
                        {
                            var s = await FindStationByQuery(session.FromStationQuery, lat, lng);
                            if (s != null) session.FromStationId = s.StationId;
                        }
                        if (!string.IsNullOrEmpty(session.ToStationQuery) && !session.ToStationId.HasValue)
                        {
                            var s = await FindStationByQuery(session.ToStationQuery, lat, lng);
                            if (s != null) session.ToStationId = s.StationId;
                        }

                        // Auto vehicle selection if 1
                        var vehicles2 = await _context.Vehicles.Where(v => v.DriverId == driverId && v.Status == 1).ToListAsync();
                        if (vehicles2.Count == 1) session.VehicleId = vehicles2[0].VehicleId;

                        SaveSessionTrip(session);

                        string summary = "";
                        if (session.FromStationId.HasValue || session.ToStationId.HasValue || session.BasePrice.HasValue || session.AvaiCapacityKg.HasValue || session.StartTime.HasValue)
                        {
                            string fromStr = session.FromStationId.HasValue ? (await _context.Stations.Include(x => x.Province).FirstOrDefaultAsync(x => x.StationId == session.FromStationId))?.Province?.ProvinceName ?? session.FromStationQuery : session.FromStationQuery;
                            string toStr = session.ToStationId.HasValue ? (await _context.Stations.Include(x => x.Province).FirstOrDefaultAsync(x => x.StationId == session.ToStationId))?.Province?.ProvinceName ?? session.ToStationQuery : session.ToStationQuery;

                            summary = "Mình đã chuẩn bị sẵn thông tin chuyến cho bạn:\n";
                            if (!string.IsNullOrEmpty(fromStr)) summary += $"- Từ: {fromStr}\n";
                            if (!string.IsNullOrEmpty(toStr)) summary += $"- Đến: {toStr}\n";
                            if (session.BasePrice.HasValue) summary += $"- Giá cơ bản: {session.BasePrice:N0}đ\n";
                            if (session.StartTime.HasValue) summary += $"- Khởi hành: {session.StartTime:dd/MM HH:mm}\n";
                            if (session.AvaiCapacityKg.HasValue) summary += $"- Trọng tải trống: {session.AvaiCapacityKg}kg\n";

                            summary += "\nĐể bạn kiểm tra và bổ sung cho dễ, mình sẽ đưa bạn sang trang tạo chuyến.\nMình vẫn tiếp tục hỗ trợ bạn trong quá trình này 👍\n\n👉 Bạn bấm bên dưới để tiếp tục nhé\n[[OPEN_CREATE_TRIP_FORM]]";
                        }
                        else
                        {
                            summary = "Mình có thể giúp bạn tạo chuyến mới 🚚\n\nĐể bạn nhập thông tin đầy đủ và dễ theo dõi hơn, mình sẽ mở trang tạo chuyến.\nTrong lúc đó, mình vẫn ở đây để hỗ trợ bạn nếu cần 👍\n\n👉 Bạn bấm nút bên dưới để bắt đầu nhé\n[[OPEN_CREATE_TRIP_FORM]]";
                        }
                        
                        return Json(new { success = true, reply = summary });
                    }

                    // Processing Steps
                    string stepInfo = "";

                    // STEP 1: VEHICLE
                    if (session.CurrentStep == TripStep.AskVehicleAndType)
                    {
                        var vehicles = await _context.Vehicles.Include(v => v.VehicleType).Where(v => v.DriverId == driverId && v.Status == 1).ToListAsync();
                        if (vehicles.Count == 0)
                        {
                            session.IsActive = false;
                            SaveSessionTrip(session);
                            return Json(new { success = true, reply = "Bạn chưa có xe nào được duyệt trong hệ thống. Vui lòng vào Quản lý xe để thêm mới trước khi đăng chuyến." });
                        }
                        else if (vehicles.Count == 1)
                        {
                            session.VehicleId = vehicles[0].VehicleId;
                            session.CurrentStep = TripStep.AskRoute;
                            stepInfo += $"Hệ thống đã tự động chọn xe biển số {vehicles[0].PlateNumber} của bạn.\n";
                        }
                        else if (session.VehicleId == null)
                        {
                            // Try to match plate number
                            var plateMatch = vehicles.FirstOrDefault(v => normUserMsg.Contains(v.PlateNumber.ToLower().Replace("-", "").Replace(".", "")));
                            if (plateMatch != null) {
                                session.VehicleId = plateMatch.VehicleId;
                                session.CurrentStep = TripStep.AskRoute;
                            } else {
                                string vList = string.Join("\n", vehicles.Select(v => $"- Biển {v.PlateNumber} ({v.CapacityKg}kg)"));
                                replyText = $"Bạn muốn đăng chuyến bằng xe nào?\n{vList}";
                                SaveSessionTrip(session);
                                return Json(new { success = true, reply = replyText });
                            }
                        }
                        else
                        {
                            session.CurrentStep = TripStep.AskRoute;
                        }
                    }

                    // STEP 2: ROUTE
                    if (session.CurrentStep == TripStep.AskRoute)
                    {
                        if (!session.FromStationId.HasValue && !string.IsNullOrEmpty(session.FromStationQuery))
                        {
                            var s = await FindStationByQuery(session.FromStationQuery, lat, lng);
                            if (s != null) session.FromStationId = s.StationId;
                            else stepInfo += $"Không tìm thấy trạm xuất phát phù hợp với '{session.FromStationQuery}'.\n";
                        }
                        
                        if (!session.ToStationId.HasValue && !string.IsNullOrEmpty(session.ToStationQuery))
                        {
                            var s = await FindStationByQuery(session.ToStationQuery, lat, lng);
                            if (s != null) session.ToStationId = s.StationId;
                            else stepInfo += $"Không tìm thấy trạm đến phù hợp với '{session.ToStationQuery}'.\n";
                        }

                        // Resolve intermediate
                        foreach (var iq in session.IntermediateQueries.ToList())
                        {
                            var s = await FindStationByQuery(iq, lat, lng);
                            if (s != null && !session.IntermediateStationIds.Contains(s.StationId))
                            {
                                session.IntermediateStationIds.Add(s.StationId);
                            }
                        }

                        if (!session.FromStationId.HasValue || !session.ToStationId.HasValue)
                        {
                            replyText = stepInfo + "Vui lòng cho mình biết **Trạm xuất phát** và **Trạm đến** của bạn.";
                            if (isFormPage)
                            {
                                replyText += " Hệ thống đã tự động điền nếu có thông tin, nhưng bạn hãy nhấn vào ô Điểm xuất phát và Điểm đến để xác nhận trạm gần nhất với bạn nhé! [[FOCUS_PROVINCE_START]]";
                            }
                            SaveSessionTrip(session);
                            return Json(new { success = true, reply = replyText });
                        }

                        // Hỏi xem có trạm phụ không
                        if (!session.RouteIsDone)
                        {
                            if (Regex.IsMatch(normUserMsg, @"\b(khong|xong|du roi|bo qua|het roi)\b"))
                            {
                                session.RouteIsDone = true;
                                session.CurrentStep = TripStep.AskTimeAndPrice;
                            }
                            else
                            {
                                var fromS = await _context.Stations.Include(x => x.Province).FirstOrDefaultAsync(x => x.StationId == session.FromStationId);
                                var toS = await _context.Stations.Include(x => x.Province).FirstOrDefaultAsync(x => x.StationId == session.ToStationId);
                                
                                string intermediateStr = "";
                                if (session.IntermediateStationIds.Any()) {
                                    var inters = await _context.Stations.Where(x => session.IntermediateStationIds.Contains(x.StationId)).ToListAsync();
                                    intermediateStr = "\nCác trạm dừng: " + string.Join(", ", inters.Select(x => x.StationName));
                                }

                                replyText = stepInfo + $"Lộ trình: **{fromS?.StationName}** -> **{toS?.StationName}**.{intermediateStr}\nBạn có muốn thêm điểm dừng dọc đường (trạm phụ) nào không?";
                                SaveSessionTrip(session);
                                return Json(new { success = true, reply = replyText });
                            }
                        }
                    }

                    // STEP 3: TIME & PRICE
                    if (session.CurrentStep == TripStep.AskTimeAndPrice)
                    {
                        var vehicle = await _context.Vehicles.FindAsync(session.VehicleId);

                        if (!session.AvaiCapacityKg.HasValue)
                        {
                            if (Regex.IsMatch(normUserMsg, @"\b(toi da|day|het|toan bo|dung)\b"))
                            {
                                session.AvaiCapacityKg = vehicle.CapacityKg;
                            }
                            else
                            {
                                replyText = $"Tải trọng tối đa của xe là **{vehicle.CapacityKg}kg**. Bạn muốn đăng chuyến này với mức tải trọng trống bao nhiêu? (Trường hợp để trống sẽ lấy mức tối đa).";
                                SaveSessionTrip(session);
                                return Json(new { success = true, reply = replyText });
                            }
                        }

                        if (!session.BasePrice.HasValue)
                        {
                            replyText = "Vui lòng cho biết **Giá cơ bản** của chuyến xe này (VNĐ).";
                            SaveSessionTrip(session);
                            return Json(new { success = true, reply = replyText });
                        }

                        if (!session.StartTime.HasValue)
                        {
                            session.StartTime = DateTime.Now;
                        }

                        // Tính OSRM
                        if (!session.EstDistance.HasValue)
                        {
                            var coords = new List<(double lat, double lng)>();
                            var fromS = await _context.Stations.FindAsync(session.FromStationId);
                            if (fromS != null) coords.Add(((double)fromS.Latitude, (double)fromS.Longitude));
                            
                            foreach(var interId in session.IntermediateStationIds) {
                                var s = await _context.Stations.FindAsync(interId);
                                if (s != null) coords.Add(((double)s.Latitude, (double)s.Longitude));
                            }

                            var toS = await _context.Stations.FindAsync(session.ToStationId);
                            if (toS != null) coords.Add(((double)toS.Latitude, (double)toS.Longitude));

                            var routing = await _routingService.GetRouteAsync(coords);
                            session.EstDistance = routing.distanceKm;
                            session.EstDurationSec = routing.durationSeconds;
                            session.EstArrivalTime = session.StartTime.Value.AddSeconds(session.EstDurationSec.Value);
                        }

                        session.CurrentStep = TripStep.Confirm;
                    }

                    // STEP 4: CONFIRM
                    if (session.CurrentStep == TripStep.Confirm)
                    {
                        var fromS = await _context.Stations.FindAsync(session.FromStationId);
                        var toS = await _context.Stations.FindAsync(session.ToStationId);
                        var platformFeeConfig = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.KeyName == "PlatformFee");
                        decimal platformFeeRate = platformFeeConfig?.Value ?? 0.05m;
                        decimal fee = session.BasePrice.Value * platformFeeRate;

                        string summary = $"**XÁC NHẬN ĐĂNG CHUYẾN XE**\n" +
                                         $"- Lộ trình: {fromS?.StationName} -> {toS?.StationName}\n" +
                                         $"- Khoảng cách ước tính: {session.EstDistance:N1} km\n" +
                                         $"- Giờ khởi hành: {session.StartTime:dd/MM HH:mm}\n" +
                                         $"- Giờ đến dự kiến: {session.EstArrivalTime:dd/MM HH:mm}\n" +
                                         $"- Tải trọng: {session.AvaiCapacityKg}kg\n" +
                                         $"- Giá cơ bản: {session.BasePrice:N0}đ\n" +
                                         $"- Phí nền tảng hệ thống: {fee:N0}đ\n\n";

                        string jsonPayload = JsonSerializer.Serialize(session);
                        string confirmCmd = $"[[DRIVER_CONFIRM_TRIP:{Uri.EscapeDataString(jsonPayload)}]]";

                        replyText = summary + "Nếu mọi thông tin đã chính xác, vui lòng bấm Xác nhận.\n\n" + confirmCmd;
                        
                        SaveSessionTrip(session);
                        return Json(new { success = true, reply = replyText });
                    }
                }

                // Fallback for General message
                return Json(new { success = true, reply = replyText });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, reply = "Hệ thống đang bận hoặc gặp lỗi khi xử lý: " + ex.Message });
            }
        }

        private async Task<Station> FindStationByQuery(string query, double? lat, double? lng)
        {
            query = RemoveDiacritics(query.ToLower());
            if (query == "gan day" || query == "hien tai")
            {
                if (lat.HasValue && lng.HasValue)
                {
                    return FindNearestStation(lat.Value, lng.Value);
                }
            }

            var stations = await _context.Stations.Include(s => s.Province).ToListAsync();
            // Match Name
            var match = stations.FirstOrDefault(s => RemoveDiacritics(s.StationName.ToLower()).Contains(query));
            if (match != null) return match;

            // Match Province
            var provMatch = stations.Where(s => RemoveDiacritics(s.Province.ProvinceName.ToLower()).Contains(query)).ToList();
            if (provMatch.Count == 1) return provMatch[0]; // If only 1 station in province
            
            // Nếu có nhiều trạm trong tỉnh đó, lấy ngẫu nhiên 1 trạm hoặc trạm đầu tiên (thực tế nên list ra cho họ chọn)
            if (provMatch.Any()) return provMatch.First();

            return null;
        }

        [HttpPost]
        public async Task<IActionResult> ExecuteDriverAction(int reqId, int actionCode)
        {
            try
            {
                var userIdStr = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userIdStr)) return Json(new { success = false, message = "Bạn cần đăng nhập." });
                int driverId = int.Parse(userIdStr);

                var request = await _context.Shiprequests
                                      .Include(r => r.Trip)
                                      .Include(r => r.Cargodetails)
                                      .FirstOrDefaultAsync(r => r.Id == reqId);

                if (request != null && request.Trip != null && request.Trip.DriverId == driverId)
                {
                    if (request.Status == 0 && (actionCode == 1 || actionCode == 2))
                    {
                        request.Status = actionCode;

                        if (actionCode == 1) // Nhận đơn
                        {
                            request.Trip.AvaiCapacityKg -= (int)(request.Cargodetails.FirstOrDefault()?.Weight ?? 0);
                            
                            // Tạo group chat
                            var existingSession = await _context.Chatsessions.FirstOrDefaultAsync(s => s.ReqId == request.Id);
                            if (existingSession == null)
                            {
                                var newSession = new Chatsession
                                {
                                    ReqId = request.Id,
                                    CustomerId = request.UserId,
                                    DriverId = driverId,
                                    CreatedAt = DateTime.Now,
                                    Status = 0
                                };
                                _context.Chatsessions.Add(newSession);
                                await _context.SaveChangesAsync();

                                var welcomeMsg = new Chatmessage
                                {
                                    SessionId = newSession.SessionId,
                                    SenderId = driverId,
                                    Message = "Đơn hàng của bạn đã được xác nhận, vui lòng trao đổi với tài xế tại đây.",
                                    SenderRole = "bot",
                                    CreatedAt = DateTime.Now
                                };
                                _context.Chatmessages.Add(welcomeMsg);
                            }
                        }
                        
                        await _context.SaveChangesAsync();
                        string msg = actionCode == 1 ? "Đã NHẬN đơn hàng thành công!" : "Đã TỪ CHỐI đơn hàng.";
                        return Json(new { success = true, message = msg });
                    }
                }
                return Json(new { success = false, message = "Không tìm thấy đơn hàng hoặc đơn hàng không còn hợp lệ." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult SyncFormToChatbot([FromBody] ChatTripSession data)
        {
            var session = GetSessionTrip();
            if (!session.IsActive) return Json(new { success = false });

            bool changed = false;
            
            if (data.VehicleId.HasValue && data.VehicleId != session.VehicleId) { session.VehicleId = data.VehicleId; changed = true; }
            if (data.RouteType > 0 && data.RouteType != session.RouteType) { session.RouteType = data.RouteType; changed = true; }
            if (data.AvaiCapacityKg.HasValue && data.AvaiCapacityKg != session.AvaiCapacityKg) { session.AvaiCapacityKg = data.AvaiCapacityKg; changed = true; }
            if (data.BasePrice.HasValue && data.BasePrice != session.BasePrice) { session.BasePrice = data.BasePrice; changed = true; }
            if (data.StartTime.HasValue && data.StartTime != session.StartTime) { session.StartTime = data.StartTime; changed = true; }
            if (data.FromStationId.HasValue && data.FromStationId != session.FromStationId) { session.FromStationId = data.FromStationId; changed = true; }
            if (data.ToStationId.HasValue && data.ToStationId != session.ToStationId) { session.ToStationId = data.ToStationId; changed = true; }

            if (changed)
            {
                // Nếu các dữ liệu cơ bản đã điền đủ, có thể đẩy step lên
                if (session.VehicleId.HasValue && session.BasePrice.HasValue && session.AvaiCapacityKg.HasValue && session.StartTime.HasValue && session.FromStationId.HasValue && session.ToStationId.HasValue)
                {
                    session.CurrentStep = TripStep.Confirm;
                }
                SaveSessionTrip(session);
            }

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetDriverSessionData()
        {
            var session = GetSessionTrip();
            if (!session.IsActive) return Json(null);

            int? fromProvId = null;
            int? toProvId = null;

            if (session.FromStationId.HasValue) 
                fromProvId = (await _context.Stations.FindAsync(session.FromStationId.Value))?.ProvinceId;
            if (session.ToStationId.HasValue) 
                toProvId = (await _context.Stations.FindAsync(session.ToStationId.Value))?.ProvinceId;

            return Json(new {
                isActive = session.IsActive,
                linkedReqId = session.LinkedReqId,
                vehicleId = session.VehicleId,
                routeType = session.RouteType,
                fromProvinceId = fromProvId,
                fromStationId = session.FromStationId,
                toProvinceId = toProvId,
                toStationId = session.ToStationId,
                basePrice = session.BasePrice,
                avaiCapacityKg = session.AvaiCapacityKg,
                startTime = session.StartTime?.ToString("yyyy-MM-ddTHH:mm")
            });
        }

        [HttpPost]
        public async Task<IActionResult> ExecuteCreateTrip()
        {
            try
            {
                var userIdStr = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userIdStr)) return Json(new { success = false, message = "Bạn cần đăng nhập." });
                int driverId = int.Parse(userIdStr);

                var session = GetSessionTrip();
                if (!session.IsActive || session.CurrentStep != TripStep.Confirm || !session.VehicleId.HasValue)
                {
                    return Json(new { success = false, message = "Phiên tạo chuyến không hợp lệ hoặc đã hết hạn." });
                }

                var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == session.VehicleId && v.DriverId == driverId && v.Status == 1);
                if (vehicle == null) return Json(new { success = false, message = "Xe không hợp lệ hoặc chưa được duyệt." });

                using var transaction = await _context.Database.BeginTransactionAsync();
                
                var trip = new Trip
                {
                    DriverId = driverId,
                    VehicleId = session.VehicleId.Value,
                    RouteType = session.RouteType,
                    FromStation = session.FromStationId.Value,
                    ToStation = session.ToStationId.Value,
                    StartTime = session.StartTime ?? DateTime.Now,
                    EstArrivalTime = session.EstArrivalTime,
                    AvaiCapacityKg = session.AvaiCapacityKg ?? vehicle.CapacityKg,
                    BasePrice = session.BasePrice ?? 0,
                    Distance = (decimal?)session.EstDistance,
                    Status = 0
                };

                _context.Trips.Add(trip);
                await _context.SaveChangesAsync();

                if (session.IntermediateStationIds.Any())
                {
                    for (int i = 0; i < session.IntermediateStationIds.Count; i++)
                    {
                        _context.TripStations.Add(new TripStation
                        {
                            TripId = trip.TripId,
                            StationId = session.IntermediateStationIds[i],
                            StopOrder = i + 1,
                            DistanceFromPrev = 0, // Cần OSRM theo chặng nhưng tạm để 0
                            EstArrivalTime = trip.StartTime
                        });
                    }
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                // Reset session
                SaveSessionTrip(new ChatTripSession());

                return Json(new { success = true, message = "Đã tạo chuyến xe mới thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống khi tạo chuyến: " + ex.Message });
            }
        }
    }

    public class DriverIntentResponse
    {
        public string Intent { get; set; }
        public int TargetId { get; set; }
        public string Reply { get; set; }
        public DriverTripData ExtractedTripData { get; set; }
    }

    public class DriverTripData
    {
        public int RouteType { get; set; }
        public string StartStationQuery { get; set; }
        public string EndStationQuery { get; set; }
        public List<string> IntermediateStationQueries { get; set; }
        public string StartTimeStr { get; set; }
        public int AvaiCapacityKg { get; set; }
        public decimal BasePrice { get; set; }
    }
}
