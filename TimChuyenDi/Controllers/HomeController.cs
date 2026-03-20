using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using TimChuyenDi.Models; 

namespace TimChuyenDi.Controllers
{
    public class HomeController : Controller
    {
        private readonly TimchuyendiContext _context;

        public HomeController(TimchuyendiContext context)
        {
            _context = context;
        }

       
        // Thêm tham số "page" mặc định là 1 vào hàm Index
        public IActionResult Index(int? fromLocation, int? toLocation, DateTime? startDate, int page = 1)
        {
            // 1. Lấy danh sách Tỉnh/Thành phố đẩy ra Drop box (đã sắp xếp A-Z)
            var locations = _context.Locations.OrderBy(l => l.ProvinceName).ToList();
            ViewBag.Locations = new SelectList(locations, "LocationId", "ProvinceName");

            // 2. Khởi tạo câu truy vấn
            var query = _context.Trips
                .Include(t => t.FromLocationNavigation)
                .Include(t => t.ToLocationNavigation)
                .Include(t => t.Driver)
                .Include(t => t.Vehicle)
                .AsQueryable();

            // 3. Xử lý Lọc dữ liệu
            if (fromLocation.HasValue) query = query.Where(t => t.FromLocation == fromLocation.Value);
            if (toLocation.HasValue) query = query.Where(t => t.ToLocation == toLocation.Value);
            if (startDate.HasValue) query = query.Where(t => t.StartTime.Date >= startDate.Value.Date);

            // Lọc các chuyến chưa chạy và sắp xếp theo thời gian
            query = query.Where(t => t.StartTime > DateTime.Now).OrderBy(t => t.StartTime);

            // 4. LOGIC PHÂN TRANG (PAGINATION)
            int pageSize = 4; // Số lượng chuyến xe hiển thị trên 1 trang
            int totalTrips = query.Count(); // Đếm tổng số chuyến xe thỏa mãn điều kiện
            int totalPages = (int)Math.Ceiling(totalTrips / (double)pageSize); // Tính tổng số trang

            ViewBag.TotalTrips = query.Count();

            // Dùng Skip và Take để lấy đúng dữ liệu của trang hiện tại
            var trips = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Lấy điểm trung bình của tài xế (Giữ nguyên logic cũ của bạn)
            var driverIds = trips.Select(t => t.DriverId).Distinct().ToList();
            var driverRatings = _context.Ratings.Include(r => r.Trip).Where(r => driverIds.Contains(r.Trip.DriverId)).ToList();
            var avgRatings = new Dictionary<int, double>();
            foreach (var id in driverIds)
            {
                var reviews = driverRatings.Where(r => r.Trip.DriverId == id).ToList();
                avgRatings[id] = reviews.Any() ? Math.Round(reviews.Average(r => r.Score), 1) : 0;
            }
            ViewBag.AvgRatings = avgRatings;

            // Truyền dữ liệu ra View
            ViewBag.CurrentFrom = fromLocation;
            ViewBag.CurrentTo = toLocation;
            ViewBag.CurrentDate = startDate?.ToString("yyyy-MM-dd");

            // Truyền thông tin phân trang
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(trips);
        }

        // GET: Xem chi tiết tất cả đánh giá của 1 tài xế cụ thể
        [HttpGet]
        public IActionResult DriverReviews(int driverId)
        {
            var driver = _context.Users.FirstOrDefault(u => u.UserId == driverId && u.Role == 1);
            if (driver == null) return NotFound();

            var reviews = _context.Ratings
                .Include(r => r.Customer)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.FromLocationNavigation)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.ToLocationNavigation)
                .Where(r => r.Trip.DriverId == driverId)
                .OrderByDescending(r => r.RatingId)
                .ToList();

            ViewBag.DriverName = driver.Name;
            ViewBag.AverageScore = reviews.Any() ? Math.Round(reviews.Average(r => r.Score), 1) : 0;
            ViewBag.TotalReviews = reviews.Count;

            return View(reviews);
        }

        public IActionResult Privacy()
        {
            return View();
        }
    }
}