using Microsoft.AspNetCore.Authorization;
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
    [Authorize(Roles = "1")]
    public class AdminChatController : Controller
    {
        private readonly OpenAIService _openAIService;
        private readonly TimchuyendiContext _context;

        public AdminChatController(OpenAIService openAIService, TimchuyendiContext context)
        {
            _openAIService = openAIService;
            _context = context;
        }

        private string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalizedString)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLower().Replace("đ", "d");
        }

        private string NormalizeQuery(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var res = text.ToLower();
            res = Regex.Replace(res, @"\bhn\b", "hà nội");
            res = Regex.Replace(res, @"\bđn\b|dn\b", "đà nẵng");
            res = Regex.Replace(res, @"\bhcm\b", "hồ chí minh");
            return res;
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(string userMessage, string history)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userMessage))
                {
                    return Json(new { success = false, reply = "Vui lòng nhập tin nhắn." });
                }

                string normUserMsg = RemoveDiacritics(NormalizeQuery(userMessage));

                // 1. Phân tích Time Range
                DateTime? rangeStart = null;
                DateTime? rangeEnd = null;
                if (Regex.IsMatch(normUserMsg, @"hom nay")) { rangeStart = DateTime.Now.Date; rangeEnd = DateTime.Now.Date.AddDays(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normUserMsg, @"toi nay")) { rangeStart = DateTime.Now.Date.AddHours(18); if (rangeStart < DateTime.Now) rangeStart = DateTime.Now; rangeEnd = DateTime.Now.Date.AddDays(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normUserMsg, @"ngay mai")) { rangeStart = DateTime.Now.Date.AddDays(1); rangeEnd = rangeStart.Value.AddDays(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normUserMsg, @"ngay kia")) { rangeStart = DateTime.Now.Date.AddDays(2); rangeEnd = rangeStart.Value.AddDays(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normUserMsg, @"tuan nay")) { rangeStart = DateTime.Now.Date; int diff = (7 - (int)DateTime.Now.DayOfWeek) % 7; rangeEnd = DateTime.Now.Date.AddDays(diff + 1).AddSeconds(-1); }
                else if (Regex.IsMatch(normUserMsg, @"tuan sau"))
                {
                    int daysUntilMonday = ((int)DayOfWeek.Monday - (int)DateTime.Now.DayOfWeek + 7) % 7;
                    if (daysUntilMonday == 0) daysUntilMonday = 7;
                    rangeStart = DateTime.Now.Date.AddDays(daysUntilMonday);
                    rangeEnd = rangeStart.Value.AddDays(7).AddSeconds(-1);
                }
                else if (Regex.IsMatch(normUserMsg, @"thang nay")) { rangeStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1); rangeEnd = rangeStart.Value.AddMonths(1).AddSeconds(-1); }
                else if (Regex.IsMatch(normUserMsg, @"thang sau"))
                {
                    rangeStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(1);
                    rangeEnd = rangeStart.Value.AddMonths(1).AddSeconds(-1);
                }
                else
                {
                    var dateYearMatch = Regex.Match(normUserMsg, @"ngay (\d{1,2})[/-](\d{1,2})[/-](\d{4})");
                    if (dateYearMatch.Success && int.TryParse(dateYearMatch.Groups[1].Value, out int d2) && int.TryParse(dateYearMatch.Groups[2].Value, out int m2) && int.TryParse(dateYearMatch.Groups[3].Value, out int y2))
                    {
                        try { rangeStart = new DateTime(y2, m2, d2); rangeEnd = rangeStart.Value.AddDays(1).AddSeconds(-1); } catch { }
                    }
                    else
                    {
                        var dateMatch = Regex.Match(normUserMsg, @"ngay (\d{1,2})[/-](\d{1,2})");
                        if (dateMatch.Success && int.TryParse(dateMatch.Groups[1].Value, out int d) && int.TryParse(dateMatch.Groups[2].Value, out int m))
                        {
                            try { rangeStart = new DateTime(DateTime.Now.Year, m, d); rangeEnd = rangeStart.Value.AddDays(1).AddSeconds(-1); } catch { }
                        }
                    }
                }

                // 2. Yêu cầu AI phân tích Intent
                string intentPrompt = @"Bạn là công cụ phân tích ngôn ngữ tự nhiên thành JSON cho Admin. Đọc tin nhắn của Admin và trả về ĐÚNG MỘT JSON hợp lệ (KHÔNG CÓ MARKDOWN HOẶC BACKTICKS), dựa trên định dạng sau:
{
  ""Intent"": ""COUNT|SUM|TOP|DETAIL|LOOKUP|CONFIG|GENERAL"",
  ""Action"": ""ADD|UPDATE|DELETE|NONE"", // Hành động cụ thể (đặc biệt dùng cho CONFIG: thêm, sửa, xóa)
  ""Target"": ""Order|Trip|Driver|Route|User|Station|Vehicle|SystemConfig|CargoType|VehicleType|TripType|None"",
  ""Status"": -1, // Số nguyên: 0 (chờ), 1 (đã duyệt/nhận), 2 (từ chối/hoàn thành xe), 3 (đang chạy đơn), 4 (hoàn thành đơn). Dùng -1 nếu không có.
  ""Keyword"": """", // SĐT, tên, biển số, mã tìm kiếm
  ""FromPoint"": """", // Tỉnh/điểm đi (nếu có)
  ""ToPoint"": """", // Tỉnh/điểm đến (nếu có)
  ""ConfigData"": {
      ""Id"": 0, // Dùng cho sửa/xóa CargoType, VehicleType, TripType
      ""KeyName"": """", // Dùng cho SystemConfig (VD: MinPrice, PlatformFee, VolumeToWeightFactor)
      ""Value"": 0, // Giá trị cấu hình hệ thống
      ""TypeName"": """", // Tên loại hàng/loại xe
      ""PriceMultiplier"": 1.0 // Hệ số giá
  }
}

Ví dụ:
- ""Đếm số đơn hàng hoàn thành hôm nay"" -> Intent: COUNT, Target: Order, Status: 4
- ""Doanh thu tuần này"" -> Intent: SUM, Target: Order (tính tổng TotalPrice)
- ""Top tài xế nhiều chuyến nhất"" -> Intent: TOP, Target: Driver
- ""Tìm đơn chưa ghép chuyến"" -> Intent: DETAIL, Target: Order, Status: 0
- ""Cập nhật phí nền tảng thành 0.2"" -> Intent: CONFIG, Target: SystemConfig, ConfigData: {KeyName: ""PlatformFee"", Value: 0.2}
- ""Tra cứu khách hàng 0901234567"" -> Intent: LOOKUP, Target: User, Keyword: ""0901234567""
- ""Xin chào"" -> Intent: GENERAL";

                string intentAiReply = await _openAIService.SendMessageAsync($"{intentPrompt}\n\nUSER MESSAGE: {userMessage}");
                
                string cleanedJson = intentAiReply.Trim();
                if (cleanedJson.StartsWith("```json")) cleanedJson = cleanedJson.Substring(7);
                if (cleanedJson.StartsWith("```")) cleanedJson = cleanedJson.Substring(3);
                if (cleanedJson.EndsWith("```")) cleanedJson = cleanedJson.Substring(0, cleanedJson.Length - 3);
                cleanedJson = cleanedJson.Trim();

                AdminIntent parsedIntent;
                try
                {
                    parsedIntent = JsonSerializer.Deserialize<AdminIntent>(cleanedJson);
                }
                catch
                {
                    // Fallback to GENERAL
                    parsedIntent = new AdminIntent { Intent = "GENERAL" };
                }

                // 3. Thực thi Logic dựa trên Intent
                string dbResultText = "";
                
                if (parsedIntent.Intent == "CONFIG")
                {
                    string targetConfig = parsedIntent.Target;
                    var configData = parsedIntent.ConfigData;
                    if (configData != null && !string.IsNullOrEmpty(targetConfig))
                    {
                        bool exists = false;
                        if (targetConfig == "SystemConfig" && !string.IsNullOrEmpty(configData.KeyName))
                        {
                            var keyLower = configData.KeyName.ToLower();
                            exists = await _context.SystemConfigs.AnyAsync(c => c.KeyName.ToLower() == keyLower);
                        }
                        else if (targetConfig == "CargoType" && !string.IsNullOrEmpty(configData.TypeName))
                        {
                            var nameLower = configData.TypeName.ToLower();
                            exists = await _context.Cargotypes.AnyAsync(c => c.TypeName.ToLower().Contains(nameLower));
                        }
                        else if (targetConfig == "VehicleType" && !string.IsNullOrEmpty(configData.TypeName))
                        {
                            var nameLower = configData.TypeName.ToLower();
                            exists = await _context.VehicleTypes.AnyAsync(v => v.TypeName.ToLower().Contains(nameLower));
                        }
                        else if (targetConfig == "TripType" && !string.IsNullOrEmpty(configData.TypeName))
                        {
                            var nameLower = configData.TypeName.ToLower();
                            exists = await _context.TripTypes.AnyAsync(t => t.Type.ToLower().Contains(nameLower));
                        }

                        string jsonPayload = JsonSerializer.Serialize(new {
                            Target = targetConfig,
                            Action = parsedIntent.Action,
                            Data = configData
                        });
                        string encodedJson = Uri.EscapeDataString(jsonPayload);

                        string confirmText = "";
                        string actionType = (parsedIntent.Action ?? "").ToUpper();
                        string itemName = targetConfig == "SystemConfig" ? configData.KeyName : configData.TypeName;
                        if (string.IsNullOrEmpty(itemName)) itemName = "này";

                        if (actionType == "DELETE")
                        {
                            if (exists)
                            {
                                confirmText = $"Bạn có chắc chắn muốn **XÓA** cấu hình **{itemName}** khỏi hệ thống không?";
                            }
                            else
                            {
                                return Json(new { success = true, reply = $"Không tìm thấy cấu hình **{itemName}** trong hệ thống để xóa." });
                            }
                        }
                        else
                        {
                            if (exists)
                            {
                                if (targetConfig == "SystemConfig")
                                    confirmText = $"Bạn có muốn cập nhật cấu hình hệ thống **{configData.KeyName}** thành **{configData.Value}** không?";
                                else if (targetConfig == "CargoType")
                                    confirmText = $"Bạn có muốn cập nhật loại hàng hoá **{configData.TypeName}** với hệ số **{configData.PriceMultiplier}** không?";
                                else if (targetConfig == "VehicleType")
                                    confirmText = $"Bạn có muốn cập nhật loại xe **{configData.TypeName}** không?";
                                else if (targetConfig == "TripType")
                                    confirmText = $"Bạn có muốn cập nhật loại hình chuyến xe **{configData.TypeName}** với hệ số **{configData.PriceMultiplier}** không?";
                                else
                                    confirmText = $"Bạn có muốn thực hiện thay đổi cấu hình đối với **{targetConfig}** không?";
                            }
                            else
                            {
                                confirmText = $"Không tìm thấy cấu hình **{itemName}** trong hệ thống. Bạn có muốn thêm mới cấu hình này không?";
                            }
                        }

                        string replyText = confirmText + $"\n\n[[CONFIRM_CONFIG_ACTION:{encodedJson}]]";
                        return Json(new { success = true, reply = replyText });
                    }
                }
                else if (parsedIntent.Intent == "GENERAL")
                {
                    string aiReply = await _openAIService.SendMessageAsync($"Bạn là Trợ Gió Admin, trợ lý cho quản trị viên. Hãy trả lời câu hỏi sau một cách tự nhiên và ngắn gọn:\n{userMessage}");
                    return Json(new { success = true, reply = aiReply });
                }
                else
                {
                    dbResultText = await ExecuteQueries(parsedIntent, rangeStart, rangeEnd);
                }

                // 4. Định dạng lại kết quả cho tự nhiên bằng AI
                string finalInstruction = $"Bạn là Trợ Gió Admin. Tôi đã truy vấn cơ sở dữ liệu dựa trên yêu cầu của quản trị viên và nhận được dữ liệu sau:\n\n{dbResultText}\n\nHãy trả lời lại cho Admin một cách tự nhiên, rõ ràng, dễ đọc (sử dụng list, in đậm nếu cần). Trả lời NGẮN GỌN, đi thẳng vào vấn đề. TUYỆT ĐỐI KHÔNG thêm các câu sáo rỗng như 'Nếu cần thêm thông tin chi tiết, vui lòng liên hệ' ở cuối. Nếu dữ liệu rỗng, báo cáo là không tìm thấy.";
                string finalAiReply = await _openAIService.SendMessageAsync($"{finalInstruction}\n\nUSER MESSAGE: {userMessage}");

                return Json(new { success = true, reply = finalAiReply });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, reply = "Lỗi hệ thống: " + ex.Message });
            }
        }

        private async Task<string> ExecuteQueries(AdminIntent intent, DateTime? start, DateTime? end)
        {
            StringBuilder sb = new StringBuilder();
            
            try
            {
                if (intent.Intent == "COUNT")
                {
                    if (intent.Target == "Order")
                    {
                        var query = _context.Shiprequests.AsQueryable();
                        if (start.HasValue && end.HasValue) query = query.Where(r => r.CreatedAt >= start.Value && r.CreatedAt <= end.Value);
                        if (intent.Status >= 0) query = query.Where(r => r.Status == intent.Status);
                        var count = await query.CountAsync();
                        sb.AppendLine($"Số lượng đơn hàng: {count}");
                    }
                    else if (intent.Target == "Trip")
                    {
                        var query = _context.Trips.AsQueryable();
                        if (start.HasValue && end.HasValue) query = query.Where(t => t.StartTime >= start.Value && t.StartTime <= end.Value);
                        if (intent.Status >= 0) query = query.Where(t => t.Status == intent.Status);
                        var count = await query.CountAsync();
                        sb.AppendLine($"Số lượng chuyến xe: {count}");
                    }
                }
                else if (intent.Intent == "SUM")
                {
                    if (intent.Target == "Order")
                    {
                        var query = _context.Shiprequests.Where(r => r.Status == 4); // Hoàn thành
                        if (start.HasValue && end.HasValue) query = query.Where(r => r.CreatedAt >= start.Value && r.CreatedAt <= end.Value);
                        var sum = await query.SumAsync(r => (decimal?)r.TotalPrice) ?? 0;
                        sb.AppendLine($"Doanh thu (tổng tiền các đơn hoàn thành): {sum:N0} VNĐ");
                    }
                    else if (intent.Target == "Driver" || intent.Target == "Trip") // Doanh thu tài xế hoặc phí nền tảng
                    {
                        var query = _context.Trips.Where(t => t.Status == 2); // Hoàn thành
                        if (start.HasValue && end.HasValue) query = query.Where(t => t.StartTime >= start.Value && t.StartTime <= end.Value);
                        var platformFee = await query.SumAsync(t => (decimal?)t.PlatformFee) ?? 0;
                        var driverEarn = await query.SumAsync(t => (decimal?)t.DriverEarning) ?? 0;
                        sb.AppendLine($"Tổng phí nền tảng thu được: {platformFee:N0} VNĐ");
                        sb.AppendLine($"Tổng doanh thu tài xế kiếm được: {driverEarn:N0} VNĐ");
                    }
                }
                else if (intent.Intent == "TOP")
                {
                    if (intent.Target == "Driver")
                    {
                        var topDrivers = await _context.Trips
                            .GroupBy(t => t.Driver)
                            .Select(g => new { DriverName = g.Key.Name, Phone = g.Key.Phone, Count = g.Count() })
                            .OrderByDescending(x => x.Count)
                            .Take(5)
                            .ToListAsync();
                        
                        sb.AppendLine("Top tài xế chạy nhiều chuyến nhất:");
                        foreach(var d in topDrivers) sb.AppendLine($"- {d.DriverName} ({d.Phone}): {d.Count} chuyến");
                    }
                    else if (intent.Target == "Route")
                    {
                        var topRoutes = await _context.Trips
                            .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                            .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                            .GroupBy(t => new { From = t.FromStationNavigation.Province.ProvinceName, To = t.ToStationNavigation.Province.ProvinceName })
                            .Select(g => new { Route = g.Key.From + " -> " + g.Key.To, Count = g.Count() })
                            .OrderByDescending(x => x.Count)
                            .Take(5)
                            .ToListAsync();
                        
                        sb.AppendLine("Top tuyến đường phổ biến nhất:");
                        foreach(var r in topRoutes) sb.AppendLine($"- {r.Route}: {r.Count} chuyến");
                    }
                }
                else if (intent.Intent == "DETAIL")
                {
                    if (intent.Target == "Trip")
                    {
                        var query = _context.Trips
                            .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                            .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                            .Include(t => t.Vehicle)
                            .Include(t => t.Driver)
                            .AsQueryable();
                        
                        if (!string.IsNullOrEmpty(intent.FromPoint))
                        {
                            var fp = RemoveDiacritics(intent.FromPoint.ToLower());
                            query = query.Where(t => t.FromStationNavigation.Province.ProvinceName.ToLower().Contains(fp) || t.FromStationNavigation.StationName.ToLower().Contains(fp));
                        }
                        if (!string.IsNullOrEmpty(intent.ToPoint))
                        {
                            var tp = RemoveDiacritics(intent.ToPoint.ToLower());
                            query = query.Where(t => t.ToStationNavigation.Province.ProvinceName.ToLower().Contains(tp) || t.ToStationNavigation.StationName.ToLower().Contains(tp));
                        }
                        
                        var trips = await query.Take(10).ToListAsync();
                        sb.AppendLine($"Tìm thấy {trips.Count} chuyến xe:");
                        foreach(var t in trips) {
                            sb.AppendLine($"- Mã {t.TripId}: {t.FromStationNavigation.Province.ProvinceName} -> {t.ToStationNavigation.Province.ProvinceName} | Khởi hành: {t.StartTime:dd/MM/yyyy HH:mm} | Tài xế: {t.Driver?.Name}");
                        }
                    }
                    else if (intent.Target == "Order")
                    {
                        var query = _context.Shiprequests.Include(r => r.User).AsQueryable();
                        if (intent.Status >= 0) query = query.Where(r => r.Status == intent.Status);
                        if (!string.IsNullOrEmpty(intent.Keyword))
                        {
                            query = query.Where(r => r.User.Name.Contains(intent.Keyword) || r.User.Phone.Contains(intent.Keyword));
                        }
                        else if (intent.Status == 0) // Chưa ghép chuyến
                        {
                            query = query.Where(r => r.TripId == null);
                        }
                        
                        var orders = await query.OrderByDescending(r => r.CreatedAt).Take(10).ToListAsync();
                        sb.AppendLine($"Tìm thấy {orders.Count} đơn hàng:");
                        foreach(var o in orders) {
                            sb.AppendLine($"- Mã đơn {o.OrderCode} | Khách: {o.User?.Name} ({o.User?.Phone}) | Trạng thái: {o.Status} | Tạo lúc: {o.CreatedAt:dd/MM/yyyy}");
                        }
                    }
                }
                else if (intent.Intent == "LOOKUP")
                {
                    if (intent.Target == "User")
                    {
                        var users = await _context.Users.Where(u => u.Phone.Contains(intent.Keyword) || u.Name.Contains(intent.Keyword) || u.Email.Contains(intent.Keyword)).Take(5).ToListAsync();
                        sb.AppendLine($"Tra cứu người dùng (từ khoá '{intent.Keyword}'):");
                        foreach(var u in users) sb.AppendLine($"- ID {u.UserId} | Tên: {u.Name} | SĐT: {u.Phone} | Vai trò: {(u.Role == 1 ? "Admin" : u.Role == 2 ? "Customer" : "Driver")}");
                    }
                    else if (intent.Target == "Station")
                    {
                        var stations = await _context.Stations.Include(s => s.Province).Where(s => s.StationName.Contains(intent.Keyword) || s.Address.Contains(intent.Keyword)).Take(5).ToListAsync();
                        sb.AppendLine($"Tra cứu trạm (từ khoá '{intent.Keyword}'):");
                        foreach(var s in stations) sb.AppendLine($"- Trạm {s.StationName} | Tỉnh: {s.Province?.ProvinceName} | Địa chỉ: {s.Address}");
                    }
                    else if (intent.Target == "Vehicle")
                    {
                        var vehicles = await _context.Vehicles.Include(v => v.Driver).Where(v => v.PlateNumber.Contains(intent.Keyword)).Take(5).ToListAsync();
                        sb.AppendLine($"Tra cứu phương tiện (từ khoá '{intent.Keyword}'):");
                        foreach(var v in vehicles) sb.AppendLine($"- ID {v.VehicleId} | Biển số: {v.PlateNumber} | Tài xế: {v.Driver?.Name} | Tải trọng: {v.CapacityKg}kg");
                    }
                }
            }
            catch(Exception ex)
            {
                sb.AppendLine("Lỗi khi truy vấn dữ liệu: " + ex.Message);
            }

            if (sb.Length == 0) sb.AppendLine("Không có dữ liệu phù hợp.");
            return sb.ToString();
        }

        [HttpPost]
        public async Task<IActionResult> ExecuteConfigAction([FromBody] ConfigPayload payload)
        {
            try
            {
                if (payload == null || string.IsNullOrEmpty(payload.Target))
                    return Json(new { success = false, message = "Dữ liệu cấu hình không hợp lệ." });

                string actionType = (payload.Action ?? "").ToUpper();

                if (payload.Target == "SystemConfig")
                {
                    var keyLower = payload.Data.KeyName.ToLower();
                    var config = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.KeyName.ToLower() == keyLower);
                    
                    if (actionType == "DELETE")
                    {
                        if (config != null) _context.SystemConfigs.Remove(config);
                    }
                    else
                    {
                        if (config != null)
                        {
                            config.Value = payload.Data.Value;
                            _context.SystemConfigs.Update(config);
                        }
                        else
                        {
                            _context.SystemConfigs.Add(new SystemConfig { KeyName = payload.Data.KeyName, Value = payload.Data.Value });
                        }
                    }
                }
                else if (payload.Target == "CargoType")
                {
                    Cargotype cargo = null;
                    if (payload.Data.Id > 0) 
                        cargo = await _context.Cargotypes.FindAsync(payload.Data.Id);
                    else if (!string.IsNullOrEmpty(payload.Data.TypeName)) 
                    {
                        var nameLower = payload.Data.TypeName.ToLower();
                        cargo = await _context.Cargotypes.FirstOrDefaultAsync(c => c.TypeName.ToLower().Contains(nameLower));
                    }

                    if (actionType == "DELETE")
                    {
                        if (cargo != null) _context.Cargotypes.Remove(cargo);
                    }
                    else
                    {
                        if (cargo != null)
                        {
                            cargo.PriceMultiplier = payload.Data.PriceMultiplier;
                            _context.Cargotypes.Update(cargo);
                        }
                        else
                        {
                            _context.Cargotypes.Add(new Cargotype { TypeName = payload.Data.TypeName, PriceMultiplier = payload.Data.PriceMultiplier });
                        }
                    }
                }
                else if (payload.Target == "VehicleType")
                {
                    VehicleType vehicle = null;
                    if (payload.Data.Id > 0)
                        vehicle = await _context.VehicleTypes.FindAsync(payload.Data.Id);
                    else if (!string.IsNullOrEmpty(payload.Data.TypeName))
                    {
                        var nameLower = payload.Data.TypeName.ToLower();
                        vehicle = await _context.VehicleTypes.FirstOrDefaultAsync(v => v.TypeName.ToLower().Contains(nameLower));
                    }

                    if (actionType == "DELETE")
                    {
                        if (vehicle != null) _context.VehicleTypes.Remove(vehicle);
                    }
                    else
                    {
                        if (vehicle == null)
                        {
                            _context.VehicleTypes.Add(new VehicleType { TypeName = payload.Data.TypeName });
                        }
                    }
                }
                else if (payload.Target == "TripType")
                {
                    TripType trip = null;
                    if (payload.Data.Id > 0)
                        trip = await _context.TripTypes.FindAsync(payload.Data.Id);
                    else if (!string.IsNullOrEmpty(payload.Data.TypeName))
                    {
                        var nameLower = payload.Data.TypeName.ToLower();
                        trip = await _context.TripTypes.FirstOrDefaultAsync(t => t.Type.ToLower().Contains(nameLower));
                    }

                    if (actionType == "DELETE")
                    {
                        if (trip != null) _context.TripTypes.Remove(trip);
                    }
                    else
                    {
                        if (trip != null)
                        {
                            trip.Multiplier = payload.Data.PriceMultiplier;
                            _context.TripTypes.Update(trip);
                        }
                        else
                        {
                            _context.TripTypes.Add(new TripType { Type = payload.Data.TypeName, Multiplier = payload.Data.PriceMultiplier });
                        }
                    }
                }

                await _context.SaveChangesAsync();
                
                string successMsg = actionType == "DELETE" ? "Đã xóa cấu hình thành công!" : "Cập nhật cấu hình thành công!";
                return Json(new { success = true, message = successMsg });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi khi lưu cấu hình: " + ex.Message });
            }
        }
    }

    public class AdminIntent
    {
        public string Intent { get; set; }
        public string Action { get; set; }
        public string Target { get; set; }
        public int Status { get; set; } = -1;
        public string Keyword { get; set; }
        public string FromPoint { get; set; }
        public string ToPoint { get; set; }
        public ConfigDataModel ConfigData { get; set; }
    }

    public class ConfigDataModel
    {
        public int Id { get; set; }
        public string KeyName { get; set; }
        public decimal Value { get; set; }
        public string TypeName { get; set; }
        public decimal PriceMultiplier { get; set; }
    }

    public class ConfigPayload
    {
        public string Target { get; set; }
        public string Action { get; set; }
        public ConfigDataModel Data { get; set; }
    }
}
