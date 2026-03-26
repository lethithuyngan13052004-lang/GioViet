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
                .Include(r => r.Cargodetails)
                .Include(r => r.Shippingroutes)
                .Where(r => r.UserId == customerId)
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
        public IActionResult BookTrip(int TripId, int CargoTypeId, string ReceiverInfo, decimal Weight, decimal Length, decimal Width, decimal Height, string Description, string PickupAddress, string DeliveryAddress, string PackageName, int Quantity = 1, decimal EstimatedValue = 0, string Note = "")
        {
            var trip = _context.Trips.Find(TripId);
            var cargoType = _context.Cargotypes.Find(CargoTypeId);

            if (trip == null || Weight <= 0 || Weight > trip.AvaiCapacityKg || cargoType == null)
            {
                TempData["Error"] = "Khối lượng không hợp lệ hoặc vượt quá chỗ trống của xe!";
                return RedirectToAction("BookTrip", new { id = TripId });
            }

            var customerIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(customerIdStr)) return RedirectToAction("Login", "Auth");
            int customerId = int.Parse(customerIdStr);

            // Thuật toán tính giá: (Khối lượng * Giá gốc) * Hệ số loại hàng
            decimal basePrice = Weight * trip.BasePricePerKg;
            decimal totalPrice = basePrice * cargoType.PriceMultiplier;

            // Gộp thông tin chi tiết vào Description nếu schema chưa có trường riêng
            string fullDescription = $"[Kiện: {PackageName}] [SL: {Quantity}] [Giá trị: {EstimatedValue:N0}đ] {Description}";

            var request = new Shiprequest
            {
                UserId = customerId,
                TripId = TripId,
                TotalPrice = totalPrice,
                Status = 0,
                Note = Note, // Lưu ghi chú từ form
                CreatedAt = DateTime.Now,
                Cargodetails = new List<Cargodetail>
                {
                    new Cargodetail
                    {
                        Weight = Weight,
                        Length = Length,
                        Width = Width,
                        Height = Height,
                        Description = fullDescription
                    }
                },
                Shippingroutes = new List<Shippingroute>
                {
                    new Shippingroute
                    {
                        ReceiverName = ReceiverInfo,
                        PickupAddress = PickupAddress,
                        DeliveryAddress = DeliveryAddress,
                        PickupType = 1
                    }
                }
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

            var user = _context.Users
                .Include(u => u.Trips)
                .FirstOrDefault(u => u.UserId == customerId);

            var trip = _context.Trips.Find(id);

            if (user != null && trip != null)
            {
                // check đã lưu chưa
                if (!user.Trips.Any(t => t.TripId == id))
                {
                    user.Trips.Add(trip);
                    _context.SaveChanges();
                }
            }

            return RedirectToAction("FindTrips");
        }

        [HttpGet]
        public IActionResult MySavedRoutes()
        {
            var customerId = int.Parse(User.FindFirstValue("UserId"));

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
                .Include(r => r.Cargodetails)
                .Include(r => r.Shippingroutes)
                .Where(r => r.UserId == customerId)
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
                .Include(r => r.Cargodetails)
                .Include(r => r.Shippingroutes)
                .FirstOrDefault(r => r.Id == id && r.UserId == customerId);

            if (requestDetail == null) return NotFound("Không tìm thấy đơn hàng!");

            return View(requestDetail);
        }

        [HttpPost]
        public IActionResult CancelRequest(int reqId)
        {
            var customerId = int.Parse(User.FindFirstValue("UserId"));
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
            var customerId = int.Parse(User.FindFirstValue("UserId"));
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