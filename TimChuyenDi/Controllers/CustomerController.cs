using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TimChuyenDi.Models;

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
        public IActionResult Index()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int customerId = int.Parse(userIdStr);

            var requests = _context.Shiprequests
                .Include(r => r.Trip).ThenInclude(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.CargoType)
                .Where(r => r.CustomerId == customerId)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return View(requests);
        }

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

            var trips = query.Where(t => t.AvaiCapacityKg > 0).OrderBy(t => t.StartTime).ToList();

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

            ViewBag.CargoTypes = new SelectList(_context.Cargotypes.ToList(), "CargoTypeId", "TypeName");
            return View(trip);
        }

        [HttpPost]
        public IActionResult BookTrip(int TripId, int CargoTypeId, string ReceiverInfo, int Weight, string Size, string Description, string PickupAddress, string DeliveryAddress)
        {
            var trip = _context.Trips.Find(TripId);
            var cargoType = _context.Cargotypes.Find(CargoTypeId);

            if (trip == null || Weight <= 0 || Weight > trip.AvaiCapacityKg || cargoType == null)
            {
                TempData["Error"] = "Khối lượng không hợp lệ hoặc vượt quá chỗ trống của xe!";
                return RedirectToAction("BookTrip", new { id = TripId });
            }

            var customerId = int.Parse(User.FindFirstValue("UserId"));

            // Thuật toán tính giá siêu việt
            decimal basePrice = Weight * trip.BasePricePerKg;
            decimal totalPrice = basePrice * cargoType.PriceMultiplier;

            var request = new Shiprequest
            {
                CustomerId = customerId,
                TripId = TripId,
                CargoTypeId = CargoTypeId,
                ReceiverInfo = ReceiverInfo,
                Weight = Weight,
                Size = Size,
                Description = Description,
                PickupAddress = PickupAddress,
                DeliveryAddress = DeliveryAddress,
                BasePrice = basePrice,
                PickupFee = 0,
                DeliveryFee = 0,
                TotalPrice = totalPrice,
                Status = 0,
                CreatedAt = DateTime.Now
            };

            _context.Shiprequests.Add(request);
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Gửi yêu cầu đặt xe thành công! Vui lòng chờ tài xế xác nhận.";
            return RedirectToAction("RequestHistory");
        }

        // ==========================================
        // 4. CHỨC NĂNG LƯU TUYẾN YÊU THÍCH
        // ==========================================
        [HttpGet]
        public IActionResult SaveRoute(int id)
        {
            var customerId = int.Parse(User.FindFirstValue("UserId"));
            var existingSave = _context.Savedroutes.FirstOrDefault(sr => sr.UserId == customerId && sr.TripId == id);

            if (existingSave == null)
            {
                _context.Savedroutes.Add(new Savedroute { UserId = customerId, TripId = id });
                _context.SaveChanges();
            }

            return RedirectToAction("FindTrips");
        }

        [HttpGet]
        public IActionResult MySavedRoutes()
        {
            var customerId = int.Parse(User.FindFirstValue("UserId"));

            var savedTrips = _context.Savedroutes
                .Include(sr => sr.Trip).ThenInclude(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(sr => sr.Trip).ThenInclude(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(sr => sr.Trip).ThenInclude(t => t.Vehicle)
                .Include(sr => sr.Trip).ThenInclude(t => t.Driver)
                .Where(sr => sr.UserId == customerId)
                .Select(sr => sr.Trip)
                .ToList();

            return View(savedTrips);
        }

        // ==========================================
        // 5. QUẢN LÝ ĐƠN HÀNG CỦA KHÁCH
        // ==========================================
        [HttpGet]
        public IActionResult RequestHistory()
        {
            var customerId = int.Parse(User.FindFirstValue("UserId"));

            var requests = _context.Shiprequests
                .Include(r => r.Trip).ThenInclude(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.Driver)
                .Include(r => r.Cargotype)
                .Where(r => r.CustomerId == customerId)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return View(requests);
        }

        [HttpGet]
        public IActionResult RequestDetail(int id)
        {
            var customerId = int.Parse(User.FindFirstValue("UserId"));

            var requestDetail = _context.Shiprequests
                .Include(r => r.Trip).ThenInclude(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(r => r.Trip).ThenInclude(t => t.Driver)
                .Include(r => r.Trip).ThenInclude(t => t.Vehicle)
                .Include(r => r.Cargotype)
                .FirstOrDefault(r => r.ReqId == id && r.CustomerId == customerId);

            if (requestDetail == null) return NotFound("Không tìm thấy đơn hàng!");

            return View(requestDetail);
        }

        [HttpPost]
        public IActionResult CancelRequest(int reqId)
        {
            var customerId = int.Parse(User.FindFirstValue("UserId"));
            var request = _context.Shiprequests.FirstOrDefault(r => r.ReqId == reqId && r.CustomerId == customerId);

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
            var customerId = int.Parse(User.FindFirstValue("UserId"));
            var request = _context.Shiprequests
                               .Include(r => r.Trip).ThenInclude(t => t.Driver)
                               .FirstOrDefault(r => r.ReqId == reqId && r.CustomerId == customerId);

            if (request == null || request.Status != 4)
                return NotFound("Đơn hàng chưa hoàn thành hoặc không tồn tại!");

            return View(request);
        }

        [HttpPost]
        public IActionResult RateTrip(int reqId, int score, string comment)
        {
            var customerId = int.Parse(User.FindFirstValue("UserId"));

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
    }
}