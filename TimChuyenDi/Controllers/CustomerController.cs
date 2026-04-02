using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TimChuyenDi.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TimChuyenDi.Controllers
{
    [Authorize] // Bắt buộc đăng nhập
    public class CustomerController : Controller
    {
        private readonly TimchuyendiContext _context;

        public CustomerController(TimchuyendiContext context)
        {
            _context = context;
        }

        // ==========================================
        // 1. TRANG CHỦ: Lịch sử đặt xe của khách hàng
        // ==========================================
        public IActionResult Index() => RedirectToAction("RequestHistory");

        // ==========================================
        // 2. CHỨC NĂNG TÌM KIẾM CHUYẾN XE
        // ==========================================
        [HttpGet]
        public IActionResult FindTrips()
        {
            ViewBag.Provinces = new SelectList(_context.Provinces.OrderBy(p => p.ProvinceName).ToList(), "ProvinceId", "ProvinceName");
            return View();
        }

        [HttpPost]
        public IActionResult FindTrips(int FromProvinceId, int ToProvinceId, DateTime? StartDate)
        {
            var query = _context.Trips
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.Vehicle).ThenInclude(v => v.VehicleType)
                .Include(t => t.Driver)
                .AsQueryable();

            if (FromProvinceId > 0) query = query.Where(t => t.FromStationNavigation.ProvinceId == FromProvinceId);
            if (ToProvinceId > 0) query = query.Where(t => t.ToStationNavigation.ProvinceId == ToProvinceId);
            if (StartDate.HasValue) query = query.Where(t => t.StartTime.Date >= StartDate.Value.Date);

            var trips = query.Where(t => t.AvaiCapacityKg > 0 && t.StartTime > DateTime.Now).OrderBy(t => t.StartTime).ToList();


            ViewBag.Provinces = new SelectList(_context.Provinces.OrderBy(p => p.ProvinceName).ToList(), "ProvinceId", "ProvinceName");
            return View("FindTripsResult", trips);
        }

        // ==========================================
        // 3. CHỨC NĂNG ĐẶT CHUYẾN XE
        // ==========================================
        [HttpGet]
        public IActionResult BookTrip(int id)
        {
            var trip = _context.Trips
                .Include(t => t.Driver)
                .Include(t => t.Vehicle)
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .FirstOrDefault(t => t.TripId == id);

            if (trip == null) return NotFound();

            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userIdStr))
            {
                var user = _context.Users.Find(int.Parse(userIdStr));
                ViewBag.UserPhone = user?.Phone;
            }

            ViewBag.CargoTypes = new SelectList(_context.Cargotypes.ToList(), "CargoTypeId", "TypeName");
            return View(trip);


        }

        [HttpPost]
        public async Task<IActionResult> BookTrip(int TripId, int CargoTypeId, string ReceiverName, string ReceiverPhone,
            string SenderPhone, int PickupType, int DeliveryType, string PickupAddress, string DeliveryAddress,
            int? FromStationId, int? ToStationId, decimal Weight, decimal Length, decimal Width, decimal Height,
            string Description, DateTime? ExpectedDeliveryDate, int Quantity = 1, string Note = "")

        {
            var trip = _context.Trips
                .Include(t => t.RouteTypeNavigation)
                .Include(t => t.FromStationNavigation)
                .Include(t => t.ToStationNavigation)
                .FirstOrDefault(t => t.TripId == TripId);

            var cargoType = _context.Cargotypes.Find(CargoTypeId);

            if (trip == null || Weight <= 0 || Weight > trip.AvaiCapacityKg)
            {
                TempData["Error"] = "Dữ liệu không hợp lệ hoặc xe đã hết chỗ!";
                return RedirectToAction("BookTrip", new { id = TripId });
            }

            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var vwFactorConfig = _context.SystemConfigs.FirstOrDefault(c => c.KeyName == "VolumeToWeightFactor");
            int vwFactor = (int)(vwFactorConfig?.Value ?? 250);

            var minPriceConfig = _context.SystemConfigs.FirstOrDefault(c => c.KeyName == "MinPrice");
            decimal minPrice = minPriceConfig?.Value ?? 0;

            decimal volume = (Length * Width * Height) / 1000000m;
            decimal chargeableWeight = Math.Max(Weight, volume * vwFactor);
            decimal capacityKg = trip.Vehicle?.CapacityKg ?? 1; // avoid div by 0

            decimal basePrice = trip.BasePrice * (chargeableWeight / capacityKg);
            decimal cargoMultiplier = cargoType?.PriceMultiplier ?? 1;
            decimal tripTypeMultiplier = trip.RouteTypeNavigation?.Multiplier ?? 1;

            decimal priceAfterCargo = basePrice * tripTypeMultiplier * cargoMultiplier;
            decimal totalPrice = Math.Max(priceAfterCargo, minPrice);

            var request = new Shiprequest
            {
                UserId = customerId,
                TripId = TripId,
                TotalPrice = totalPrice,
                Status = 0,
                Note = Note,
                PickupTimeFrom = DateTime.Now,
                PickupTimeTo = ExpectedDeliveryDate ?? trip.ArrivalTime
            };

            _context.Shiprequests.Add(request);
            await _context.SaveChangesAsync();



            var cargo = new Cargodetail
            {
                RequestId = request.Id,
                Weight = Weight,
                Length = Length,
                Width = Width,
                Height = Height,
                Description = Description
            };

            _context.Cargodetails.Add(cargo);

            var route = new Shippingroute
            {
                RequestId = request.Id,
                SenderPhone = SenderPhone,
                FromProvinceId = trip.FromStationNavigation?.ProvinceId,
                ToProvinceId = trip.ToStationNavigation?.ProvinceId,
                PickupType = PickupType,
                DeliveryType = DeliveryType,
                PickupAddress = PickupAddress,
                DeliveryAddress = DeliveryAddress,
                FromStationId = FromStationId ?? trip.FromStation,
                ToStationId = ToStationId ?? trip.ToStation,
                ReceiverName = ReceiverName,
                ReceiverPhone = ReceiverPhone
            };

            // Nếu lấy hàng tại kho mà địa chỉ chuỗi đang trống -> Lấy tên của Kho/Trạm đó làm địa chỉ
            if (route.PickupType == 2 && string.IsNullOrEmpty(route.PickupAddress) && route.FromStationId.HasValue)
            {
                var st = _context.Stations.Find(route.FromStationId);
                if (st != null) route.PickupAddress = st.StationName;
            }
            // Nếu nhận hàng tại bến mà địa chỉ chuỗi đang trống -> Lấy tên của Kho/Trạm đó làm địa chỉ
            if (route.DeliveryType == 2 && string.IsNullOrEmpty(route.DeliveryAddress) && route.ToStationId.HasValue)
            {
                var st = _context.Stations.Find(route.ToStationId);
                if (st != null) route.DeliveryAddress = st.StationName;
            }


            _context.Shippingroutes.Add(route);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Đặt xe thành công!";
            return RedirectToAction("RequestHistory");
        }

        // ==========================================
        // 4. ĐĂNG TIN CHỜ XE (GET)
        // ==========================================
        [HttpGet]
        public IActionResult CreateRequest()
        {
            ViewBag.Provinces = new SelectList(_context.Provinces.OrderBy(p => p.ProvinceName), "ProvinceId", "ProvinceName");
            ViewBag.CargoTypes = new SelectList(_context.Cargotypes, "CargoTypeId", "TypeName");
            ViewBag.CurrentDate = DateTime.Now.ToString("yyyy-MM-dd");

            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userIdStr))
            {
                var user = _context.Users.Find(int.Parse(userIdStr));
                ViewBag.UserPhone = user?.Phone;
            }

            return View();
        }


        // ==========================================
        // 5. ĐĂNG TIN CHỜ XE (POST - Lấy hàng tận nơi)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> CreateRequest(int fromProvinceId, int toProvinceId, int cargoTypeId,
            string ReceiverName, string ReceiverPhone, string SenderPhone, int PickupType, int DeliveryType,
            string PickupAddress, string DeliveryAddress, int? FromStationId, int? ToStationId,
            decimal Weight, decimal Length, decimal Width, decimal Height, string Description, string Note,
            DateTime? ExpectedDeliveryDate)

        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var request = new Shiprequest
            {
                UserId = customerId,
                TripId = null,
                Status = 0,
                Note = Note,
                PickupTimeFrom = DateTime.Now,
                PickupTimeTo = ExpectedDeliveryDate
            };

            _context.Shiprequests.Add(request);
            await _context.SaveChangesAsync();



            // BƯỚC 2: Lưu hàng hóa
            var cargo = new Cargodetail
            {
                RequestId = request.Id,
                Weight = Weight,
                Length = Length,
                Width = Width,
                Height = Height,
                Description = Description
            };
            _context.Cargodetails.Add(cargo);

            // BƯỚC 3: Lưu lộ trình
            var route = new Shippingroute
            {
                RequestId = request.Id,
                SenderPhone = SenderPhone,
                FromProvinceId = fromProvinceId,
                ToProvinceId = toProvinceId,
                PickupType = PickupType,
                DeliveryType = DeliveryType,
                PickupAddress = PickupAddress,
                DeliveryAddress = DeliveryAddress,
                FromStationId = FromStationId,
                ToStationId = ToStationId,
                ReceiverName = ReceiverName,
                ReceiverPhone = ReceiverPhone
            };

            // Nếu lấy hàng tại bến mà địa chỉ đang trống -> Lấy tên bến/kho làm địa chỉ hiển thị
            if (route.PickupType == 2 && string.IsNullOrEmpty(route.PickupAddress) && route.FromStationId.HasValue)
            {
                var st = _context.Stations.Find(route.FromStationId.Value);
                if (st != null) route.PickupAddress = st.StationName;
            }
            // Nếu nhận hàng tại bến mà địa chỉ đang trống -> Lấy tên bến/kho làm địa chỉ hiển thị
            if (route.DeliveryType == 2 && string.IsNullOrEmpty(route.DeliveryAddress) && route.ToStationId.HasValue)
            {
                var st = _context.Stations.Find(route.ToStationId.Value);
                if (st != null) route.DeliveryAddress = st.StationName;
            }

            _context.Shippingroutes.Add(route);

            await _context.SaveChangesAsync();
            return RedirectToAction("RequestMatches", new { id = request.Id });
        }

        // ==========================================
        // 6. TÌM CHUYẾN XE PHÙ HỢP VỚI ĐƠN CHỜ
        // ==========================================
        [HttpGet]
        public IActionResult RequestMatches(int id)
        {
            var shipRequest = _context.Shiprequests
                .Include(r => r.Shippingroutes)
                .Include(r => r.Cargodetails)
                .FirstOrDefault(r => r.Id == id);

            if (shipRequest == null) return NotFound();

            var route = shipRequest.Shippingroutes.FirstOrDefault();
            var cargo = shipRequest.Cargodetails.FirstOrDefault();

            // Tìm xe chạy cùng tuyến Tỉnh -> Tỉnh và còn đủ chỗ
            var matchingTrips = _context.Trips
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.Driver)
                .Include(t => t.Vehicle).ThenInclude(v => v.VehicleType)
                .Where(t => t.AvaiCapacityKg >= (cargo != null ? cargo.Weight : 0)
                       && (route == null || (t.FromStationNavigation.ProvinceId == route.FromProvinceId && t.ToStationNavigation.ProvinceId == route.ToProvinceId))
                       && t.StartTime > DateTime.Now)
                .OrderBy(t => t.StartTime)
                .ToList();


            ViewBag.RequestId = id;
            return View(matchingTrips);
        }

        // ==========================================
        // 7. CHỌN CHUYẾN XE CHO ĐƠN ĐANG CHỜ
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> AssignTripToRequest(int tripId, int requestId)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var request = _context.Shiprequests
                .Include(r => r.Cargodetails)
                .FirstOrDefault(r => r.Id == requestId && r.UserId == customerId);
            
            var trip = _context.Trips
                .Include(t => t.Vehicle)
                .Include(t => t.RouteTypeNavigation)
                .FirstOrDefault(t => t.TripId == tripId);

            if (request != null && trip != null)
            {
                request.TripId = tripId;
                request.PickupTimeTo = trip.ArrivalTime;


                // Đồng bộ OrderCode nếu chưa có
                if (string.IsNullOrEmpty(request.OrderCode))
                {
                    request.OrderCode = "TC" + request.Id;
                }
                
                // Cập nhật lại giá
                var cargo = request.Cargodetails.FirstOrDefault();
                if (cargo != null)
                {
                    var vwFactorConfig = _context.SystemConfigs.FirstOrDefault(c => c.KeyName == "VolumeToWeightFactor");
                    int vwFactor = (int)(vwFactorConfig?.Value ?? 250);

                    var minPriceConfig = _context.SystemConfigs.FirstOrDefault(c => c.KeyName == "MinPrice");
                    decimal minPrice = minPriceConfig?.Value ?? 0;

                    decimal length = cargo.Length ?? 0;
                    decimal width = cargo.Width ?? 0;
                    decimal height = cargo.Height ?? 0;
                    decimal weight = cargo.Weight ?? 0;

                    decimal volume = (length * width * height) / 1000000m;
                    decimal chargeableWeight = Math.Max(weight, volume * vwFactor);
                    decimal capacityKg = trip.Vehicle?.CapacityKg ?? 1;

                    decimal basePrice = trip.BasePrice * (chargeableWeight / capacityKg);

                    decimal tripTypeMultiplier = trip.RouteTypeNavigation?.Multiplier ?? 1;
                    
                    // We don't have cargoType directly here inside Assign, so request needs to include CargoType if exists,
                    // Actually, let's just make cargoMultiplier = 1 if we don't fetch CargoType. Since it's free form, cargo doesn't link to CargoTypeId directly on model? Wait, Shiprequest might. 
                    decimal cargoMultiplier = 1;

                    decimal priceAfterCargo = basePrice * tripTypeMultiplier * cargoMultiplier;
                    request.TotalPrice = Math.Max(priceAfterCargo, minPrice);
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Đã chọn chuyến xe! Vui lòng chờ tài xế xác nhận.";
            }

            return RedirectToAction("RequestHistory");
        }

        // ==========================================
        // 8. CHỨC NĂNG LƯU TUYẾN YÊU THÍCH
        // ==========================================
        [HttpGet]
        public IActionResult SaveRoute(int id)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var user = _context.Users
                .Include(u => u.Trips)
                .FirstOrDefault(u => u.UserId == customerId);

            var trip = _context.Trips.Find(id);

            if (user != null && trip != null && !user.Trips.Any(t => t.TripId == id))
            {
                user.Trips.Add(trip);
                _context.SaveChanges();
                TempData["SuccessMessage"] = "Đã lưu tuyến xe thành công!";
            }

            return RedirectToAction("MySavedRoutes");
        }

        [HttpGet]
        public IActionResult MySavedRoutes()
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var user = _context.Users
                .Include(u => u.Trips)
                    .ThenInclude(t => t.FromStationNavigation)
                        .ThenInclude(s => s.Province)
                .Include(u => u.Trips)
                    .ThenInclude(t => t.ToStationNavigation)
                        .ThenInclude(s => s.Province)
                .Include(u => u.Trips)
                    .ThenInclude(t => t.Vehicle)
                        .ThenInclude(v => v.VehicleType)
                .Include(u => u.Trips)
                    .ThenInclude(t => t.Driver)
                .FirstOrDefault(u => u.UserId == customerId);

            var savedTrips = user?.Trips.ToList() ?? new List<Trip>();

            return View(savedTrips);
        }

        // ==========================================
        // 5. QUẢN LÝ ĐƠN HÀNG CỦA KHÁCH
        // ==========================================
        [HttpGet]
        public IActionResult RequestHistory()
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var requests = _context.Shiprequests
                .Include(r => r.Trip).ThenInclude(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.Driver)
                .Include(r => r.Cargodetails)
                .Include(r => r.Shippingroutes)
                    .ThenInclude(sr => sr.FromStation)
                .Include(r => r.Shippingroutes)
                    .ThenInclude(sr => sr.ToStation)
                .Where(r => r.UserId == customerId)
                .OrderByDescending(r => r.PickupTimeFrom)
                .ToList();


            return View(requests);
        }

        [HttpGet]
        public IActionResult RequestDetail(int id)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var requestDetail = _context.Shiprequests
                .Include(r => r.Trip).ThenInclude(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.Driver)
                .Include(r => r.Trip).ThenInclude(t => t.Vehicle).ThenInclude(v => v.VehicleType)
                .Include(r => r.Trip).ThenInclude(t => t.TripStations).ThenInclude(ts => ts.Station)
                .Include(r => r.Cargodetails)
                .Include(r => r.Shippingroutes)
                    .ThenInclude(sr => sr.FromStation)
                .Include(r => r.Shippingroutes)
                    .ThenInclude(sr => sr.ToStation)

                .FirstOrDefault(r => r.Id == id && r.UserId == customerId);

            if (requestDetail == null) return NotFound("Không tìm thấy đơn hàng!");

            return View(requestDetail);
        }

        [HttpPost]
        public IActionResult CancelRequest(int reqId)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var request = _context.Shiprequests.FirstOrDefault(r => r.Id == reqId && r.UserId == customerId);

            if (request != null && request.Status == 0)
            {
                request.Status = 5; // 5: Đã hủy
                _context.SaveChanges();
            }

            return RedirectToAction("RequestHistory");
        }

        // ==========================================
        // 6. ĐÁNH GIÁ CHUYẾN ĐI
        // ==========================================
        [HttpGet]
        public IActionResult RateTrip(int reqId)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var request = _context.Shiprequests
                               .Include(r => r.Trip).ThenInclude(t => t.Driver)
                               .FirstOrDefault(r => r.Id == reqId && r.UserId == customerId);

            if (request == null || request.Status != 4)
                return NotFound("Đơn hàng chưa hoàn thành hoặc không tồn tại!");

            return View(request);
        }

        [HttpPost]
        public IActionResult RateTrip(int reqId, int score, string comment)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var existingRating = _context.Ratings.FirstOrDefault(r => r.ReqId == reqId && r.CustomerId == customerId);

            if (existingRating == null)
            {
                var rating = new Rating
                {
                    ReqId = reqId,
                    CustomerId = customerId,
                    Score = score,
                    Comment = comment,
                    CreatedAt = DateTime.Now
                };
                _context.Ratings.Add(rating);
                _context.SaveChanges();
            }

            return RedirectToAction("RequestHistory");
        }

        // ==========================================
        // MISC
        // ==========================================


        [HttpGet]
        public IActionResult GetStations(int provinceId)

        {
            var stations = _context.Stations
                .Where(s => s.ProvinceId == provinceId)
                .Select(s => new { 
                    s.StationId, 
                    s.StationName,
                    s.Address,
                    s.Latitude,
                    s.Longitude
                })
                .ToList();
            return Json(stations);
        }

        // ==========================================
        // 11. XÁC NHẬN TẠO ĐƠN TỪ CHATBOT (AI)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> ConfirmChatOrder(int fromId, int toId, decimal weight, string desc, string phone, 
            int pType = 2, int dType = 2, string pAddr = "", string dAddr = "", int cType = 0)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int customerId = int.Parse(userIdStr);

            // 1. Tạo ShipRequest
            var request = new Shiprequest
            {
                UserId = customerId,
                Status = 0, // Chờ xe
                Note = "Đã tạo qua Trợ lý AI",
                PickupTimeFrom = DateTime.Now,
                PickupTimeTo = DateTime.Now.AddDays(3), // Mặc định 3 ngày
                TotalPrice = 0 // Sẽ tính sau khi tài xế báo giá hoặc ghép chuyến
            };
            _context.Shiprequests.Add(request);
            await _context.SaveChangesAsync();

            // 2. Tạo Cargo Detail
            var cargo = new Cargodetail
            {
                RequestId = request.Id,
                Description = desc ?? "Hàng hóa từ Chatbot",
                Weight = weight > 0 ? weight : 1,
                Length = 10, Width = 10, Height = 10 // Mặc định nhỏ
            };
            _context.Cargodetails.Add(cargo);

            // 3. Tạo Shipping Route
            var route = new Shippingroute
            {
                RequestId = request.Id,
                FromProvinceId = fromId,
                ToProvinceId = toId,
                PickupType = pType,
                DeliveryType = dType,
                PickupAddress = pAddr,
                DeliveryAddress = dAddr,
                SenderPhone = User.FindFirstValue(ClaimTypes.MobilePhone) ?? "",
                ReceiverName = "Khách hàng",
                ReceiverPhone = phone ?? ""
            };

            // Tự động tìm trạm đầu tiên của tỉnh nếu đi tại bến
            if (pType == 2 && fromId > 0)
            {
                var st = _context.Stations.FirstOrDefault(s => s.ProvinceId == fromId);
                if (st != null) {
                    route.FromStationId = st.StationId;
                    if (string.IsNullOrEmpty(pAddr)) route.PickupAddress = st.StationName;
                }
            }
            if (dType == 2 && toId > 0)
            {
                var st = _context.Stations.FirstOrDefault(s => s.ProvinceId == toId);
                if (st != null) {
                    route.ToStationId = st.StationId;
                    if (string.IsNullOrEmpty(dAddr)) route.DeliveryAddress = st.StationName;
                }
            }

            _context.Shippingroutes.Add(route);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Đơn hàng của bạn đã được tạo thành công qua Trợ lý Gió Việt!";
            return RedirectToAction("RequestHistory");
        }
    }
}