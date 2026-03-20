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

       
        public IActionResult Index(int? fromLocation, int? toLocation, DateTime? startDate, int page = 1)
{
    // Dropdown Station
    var stations = _context.Stations
        .OrderBy(s => s.StationName)
        .ToList();

    ViewBag.Stations = new SelectList(stations, "StationId", "StationName");

    // Query
    var query = _context.Trips
        .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
        .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
        .Include(t => t.Driver)
        .Include(t => t.Vehicle)
        .AsQueryable();

    // Filter
    if (fromLocation.HasValue)
        query = query.Where(t => t.FromStation == fromLocation.Value);

    if (toLocation.HasValue)
        query = query.Where(t => t.ToStation == toLocation.Value);

    if (startDate.HasValue)
        query = query.Where(t => t.StartTime.Date >= startDate.Value.Date);

    query = query.Where(t => t.StartTime > DateTime.Now)
                 .OrderBy(t => t.StartTime);

    // Pagination
    int pageSize = 4;
    int totalTrips = query.Count();
    int totalPages = (int)Math.Ceiling(totalTrips / (double)pageSize);

    ViewBag.TotalTrips = totalTrips;

    var trips = query.Skip((page - 1) * pageSize)
                     .Take(pageSize)
                     .ToList();

    // Rating
    var driverIds = trips.Select(t => t.DriverId).Distinct().ToList();

    var driverRatings = _context.Ratings
        .Include(r => r.Req)
            .ThenInclude(req => req.Trip)
        .Where(r => r.Req.Trip != null && driverIds.Contains(r.Req.Trip.DriverId))
        .ToList();

    var avgRatings = new Dictionary<int, double>();

    foreach (var id in driverIds)
    {
        var reviews = driverRatings.Where(r => r.Req.Trip.DriverId == id).ToList();

        avgRatings[id] = reviews.Any()
            ? Math.Round(reviews.Average(r => r.Score), 1)
            : 0;
    }

    ViewBag.AvgRatings = avgRatings;

    // ViewBag
    ViewBag.CurrentFrom = fromLocation;
    ViewBag.CurrentTo = toLocation;
    ViewBag.CurrentDate = startDate?.ToString("yyyy-MM-dd");

    ViewBag.CurrentPage = page;
    ViewBag.TotalPages = totalPages;

    return View(trips);
}

        // GET: Xem chi tiết tất cả đánh giá của 1 tài xế cụ thể
        [HttpGet]
        public IActionResult DriverReviews(int driverId)
        {
            var driver = _context.Users
                .FirstOrDefault(u => u.UserId == driverId && u.Role == 3); // ✅ FIX role

            if (driver == null) return NotFound();

            var reviews = _context.Ratings
                .Include(r => r.Customer)
                .Include(r => r.Req)
                    .ThenInclude(req => req.Trip)
                        .ThenInclude(t => t.FromStationNavigation)
                .Include(r => r.Req)
                    .ThenInclude(req => req.Trip)
                        .ThenInclude(t => t.ToStationNavigation)
                .Where(r => r.Req.Trip != null && r.Req.Trip.DriverId == driverId)
                .OrderByDescending(r => r.RatingId)
                .ToList();

            ViewBag.DriverName = driver.Name;

            ViewBag.AverageScore = reviews.Any()
                ? Math.Round(reviews.Average(r => r.Score), 1)
                : 0;

            ViewBag.TotalReviews = reviews.Count;

            return View(reviews);
        }

        public IActionResult Privacy()
        {
            return View();
        }
    }
}