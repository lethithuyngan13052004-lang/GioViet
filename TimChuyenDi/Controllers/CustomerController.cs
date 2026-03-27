using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TimChuyenDi.Models;

namespace TimChuyenDi.Controllers
{
    [Authorize]
    public class CustomerController : Controller
    {
        private readonly TimchuyendiContext _context;

        public CustomerController(TimchuyendiContext context)
        {
            _context = context;
        }

        // ==========================================
        // 1. LỊCH SỬ ĐƠN HÀNG (Sửa Include cho 3 bảng)
        // ==========================================
        public IActionResult RequestHistory()
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int customerId = int.Parse(userIdStr);

            var requests = _context.Shiprequests
                .Include(r => r.Trip).ThenInclude(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Cargodetails) // Nối bảng hàng hóa
                .Include(r => r.Shippingroutes) // Nối bảng lộ trình
                .Where(r => r.UserId == customerId)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return View(requests);
        }

        // ==========================================
        // 2. CHỨC NĂNG ĐẶT CHUYẾN XE (GET)
        // ==========================================
        [HttpGet]
        public IActionResult BookTrip(int id)
        {
            var trip = _context.Trips
                .Include(t => t.Driver)
                .Include(t => t.Vehicle).ThenInclude(v => v.VehicleType)
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .FirstOrDefault(t => t.TripId == id);

            if (trip == null) return NotFound();

            ViewBag.CargoTypes = new SelectList(_context.Cargotypes.ToList(), "CargoTypeId", "TypeName");
            
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userIdStr))
            {
                var user = _context.Users.Find(int.Parse(userIdStr));
                ViewBag.UserPhone = user?.Phone;
            }

            return View(trip);
        }

        // ==========================================
        // 3. ĐẶT CHUYẾN XE (POST Fix logic lưu 3 bảng)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> BookTrip(int TripId, int CargoTypeId, string ReceiverName, string ReceiverPhone,
            string SenderPhone, int PickupType, int DeliveryType, string PickupAddress, string DeliveryAddress,
            int? FromStationId, int? ToStationId, decimal Weight, decimal Length, decimal Width, decimal Height,
            string Description, decimal? PickupLat, decimal? PickupLng, decimal? DeliveryLat, decimal? DeliveryLng,
            int Quantity = 1, string Note = "")
        {
            var trip = _context.Trips.Find(TripId);
            var cargoType = _context.Cargotypes.Find(CargoTypeId);

            if (trip == null || Weight <= 0 || Weight > trip.AvaiCapacityKg)
            {
                TempData["Error"] = "Dữ liệu không hợp lệ hoặc xe đã hết chỗ!";
                return RedirectToAction("BookTrip", new { id = TripId });
            }

            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            // Tính giá sơ bộ
            decimal totalOrderPrice = Weight * trip.BasePricePerKg * (cargoType?.PriceMultiplier ?? 1);

            // BƯỚC 1: Lưu bảng cha shiprequest
            var request = new Shiprequest
            {
                UserId = customerId,
                TripId = TripId,
                TotalPrice = totalOrderPrice,
                Status = 0,
                Note = Note,
                CreatedAt = DateTime.Now
            };
            _context.Shiprequests.Add(request);
            await _context.SaveChangesAsync(); // Lưu để lấy request.Id

            // BƯỚC 2: Lưu bảng cargodetail
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

            // BƯỚC 3: Lưu bảng shippingroute
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
                ReceiverPhone = ReceiverPhone,
                Lat = PickupLat ?? DeliveryLat,
                Lng = PickupLng ?? DeliveryLng
            };
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
            decimal Weight, decimal Length, decimal Width, decimal Height, string Description, string Note)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            // BƯỚC 1: Lưu shiprequest (TripId = null vì chưa có xe)
            var request = new Shiprequest
            {
                UserId = customerId,
                TripId = null,
                Status = 0,
                Note = Note,
                CreatedAt = DateTime.Now
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
            _context.Shippingroutes.Add(route);

            await _context.SaveChangesAsync();
            return RedirectToAction("RequestMatches", new { id = request.Id });
        }

        // ==========================================
        // 4. TÌM CHUYẾN XE PHÙ HỢP VỚI ĐƠN CHỜ
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
                       && (route == null || (t.FromStationNavigation.ProvinceId == route.FromProvinceId && t.ToStationNavigation.ProvinceId == route.ToProvinceId)))
                .OrderBy(t => t.StartTime)
                .ToList();

            ViewBag.RequestId = id;
            return View(matchingTrips);
        }

        // ==========================================
        // 5. CHI TIẾT ĐƠN HÀNG
        // ==========================================
        [HttpGet]
        public IActionResult RequestDetail(int id)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var requestDetail = _context.Shiprequests
                .Include(r => r.Trip).ThenInclude(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.Driver)
                .Include(r => r.Trip).ThenInclude(t => t.Vehicle)
                .Include(r => r.Cargodetails)
                .Include(r => r.Shippingroutes)
                .FirstOrDefault(r => r.Id == id && r.UserId == customerId);

            if (requestDetail == null) return NotFound("Không tìm thấy đơn hàng!");

            return View(requestDetail);
        }

        // ==========================================
        // 6. HỦY ĐƠN HÀNG
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> CancelRequest(int reqId)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var request = _context.Shiprequests.FirstOrDefault(r => r.Id == reqId && r.UserId == customerId);

            if (request != null && request.Status == 0)
            {
                request.Status = 5; // 5: Đã hủy
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã hủy đơn hàng thành công.";
            }

            return RedirectToAction("RequestHistory");
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
            
            var trip = _context.Trips.Find(tripId);

            if (request != null && trip != null)
            {
                request.TripId = tripId;
                
                // Cập nhật lại giá dựa trên Voyage (nếu cần)
                var cargo = request.Cargodetails.FirstOrDefault();
                if (cargo != null)
                {
                    request.TotalPrice = cargo.Weight * trip.BasePricePerKg;
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã chọn chuyến xe! Vui lòng chờ tài xế xác nhận.";
            }

            return RedirectToAction("RequestHistory");
        }

        // ==========================================
        // 8. ĐÁNH GIÁ CHUYẾN ĐI
        // ==========================================
        [HttpGet]
        public IActionResult RateTrip(int reqId)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            var request = _context.Shiprequests
                .Include(r => r.Trip).ThenInclude(t => t.Driver)
                .FirstOrDefault(r => r.Id == reqId && r.UserId == customerId);
            if (request == null || request.Status != 4 || request.Trip == null)
                return NotFound("Đơn hàng chưa hoàn thành hoặc không tồn tại!");

            // Trả về Model là Trip vì view yêu cầu Trip
            ViewBag.RequestId = reqId;
            return View(request.Trip);
        }

        [HttpPost]
        public async Task<IActionResult> RateTrip(int reqId, int score, string comment)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int customerId = int.Parse(userIdStr);

            // Kiểm tra xem đơn có thuộc về khách này ko
            var request = _context.Shiprequests.FirstOrDefault(r => r.Id == reqId && r.UserId == customerId);
            if (request == null) return NotFound();

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
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("RequestHistory");
        }

        // ==========================================
        // TÌM CHUYẾN XE
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

            var trips = query.Where(t => t.AvaiCapacityKg > 0).OrderBy(t => t.StartTime).ToList();

            ViewBag.Provinces = new SelectList(_context.Provinces.OrderBy(p => p.ProvinceName).ToList(), "ProvinceId", "ProvinceName");
            return View("FindTripsResult", trips);
        }

        // ==========================================
        // LƯU TUYẾN YÊU THÍCH
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
                .Include(u => u.Trips)
                    .ThenInclude(t => t.Driver)
                .FirstOrDefault(u => u.UserId == customerId);

            var savedTrips = user?.Trips.ToList() ?? new List<Trip>();
            return View(savedTrips);
        }

        // ==========================================
        // MISC
        // ==========================================
        public IActionResult Index() => RedirectToAction("RequestHistory");

        [HttpGet]
        public IActionResult GetStations(int provinceId)
        {
            var stations = _context.Stations
                .Where(s => s.ProvinceId == provinceId)
                .Select(s => new { s.StationId, s.StationName })
                .ToList();
            return Json(stations);
        }
    }
}