using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using TimChuyenDi.Models;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace TimChuyenDi.Controllers
{
    public class HomeController : Controller
    {
        private readonly TimchuyendiContext _context;

        public HomeController(TimchuyendiContext context)
        {
            _context = context;
        }

        // ==================================================
        // 1. TRANG CHỦ & TÌM KIẾM
        // ==================================================
        public IActionResult Index(int? fromProvinceId, int? toProvinceId, DateTime? startDate, int? cargoTypeId, int page = 1)
        {
            var provinces = _context.Provinces.OrderBy(p => p.ProvinceName).ToList();
            ViewBag.Provinces = new SelectList(provinces, "ProvinceId", "ProvinceName");

            var cargoTypes = _context.Cargotypes.ToList();
            ViewBag.CargoTypes = new SelectList(cargoTypes, "CargoTypeId", "TypeName");

            var query = _context.Trips
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.Driver)
                .Include(t => t.Vehicle).ThenInclude(v => v.VehicleType)
                .AsQueryable();

            if (fromProvinceId.HasValue)
                query = query.Where(t => t.FromStationNavigation.ProvinceId == fromProvinceId.Value);

            if (toProvinceId.HasValue)
                query = query.Where(t => t.ToStationNavigation.ProvinceId == toProvinceId.Value);

            if (startDate.HasValue)
                query = query.Where(t => t.StartTime.Date >= startDate.Value.Date);

            if (cargoTypeId.HasValue)
            {
                query = query.Where(t => t.Vehicle.VehicleType.CargoTypes.Any(c => c.CargoTypeId == cargoTypeId.Value));
            }

            query = query.Where(t => t.StartTime > DateTime.Now).OrderBy(t => t.StartTime);

            int pageSize = 4;
            int totalTrips = query.Count();
            int totalPages = (int)Math.Ceiling(totalTrips / (double)pageSize);

            ViewBag.TotalTrips = totalTrips;

            var trips = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var driverIds = trips.Select(t => t.DriverId).Distinct().ToList();
            var driverRatings = _context.Ratings
                .Include(r => r.Req).ThenInclude(req => req.Trip)
                .Where(r => r.Req.Trip != null && driverIds.Contains(r.Req.Trip.DriverId))
                .ToList();

            var avgRatings = new Dictionary<int, double>();
            foreach (var id in driverIds)
            {
                var reviews = driverRatings.Where(r => r.Req.Trip.DriverId == id).ToList();
                avgRatings[id] = reviews.Any() ? Math.Round(reviews.Average(r => r.Score), 1) : 0;
            }

            ViewBag.AvgRatings = avgRatings;
            ViewBag.CurrentFrom = fromProvinceId;
            ViewBag.CurrentTo = toProvinceId;
            ViewBag.CurrentDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentCargo = cargoTypeId;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(trips);
        }

        // ==================================================
        // 2. XEM ĐÁNH GIÁ TÀI XẾ
        // ==================================================
        [HttpGet]
        public IActionResult DriverReviews(int driverId)
        {
            var driver = _context.Users.FirstOrDefault(u => u.UserId == driverId && u.Role == 3);

            if (driver == null) return NotFound();

            var reviews = _context.Ratings
                .Include(r => r.Customer)
                .Include(r => r.Req).ThenInclude(req => req.Trip).ThenInclude(t => t.FromStationNavigation)
                .Include(r => r.Req).ThenInclude(req => req.Trip).ThenInclude(t => t.ToStationNavigation)
                .Where(r => r.Req.Trip != null && r.Req.Trip.DriverId == driverId)
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

        // ==================================================
        // 3. TOOL BƠM DỮ LIỆU QUẬN/HUYỆN & XÃ/PHƯỜNG (TỪ CSV)
        // ==================================================
        [HttpGet]
        public IActionResult ImportLocations()
        {
            string html = @"
                <div style='margin: 50px; font-family: Arial;'>
                    <h2>Tool Import 10.000+ Xã/Phường Toàn Quốc</h2>
                    <form method='post' enctype='multipart/form-data' action='/Home/ImportLocations'>
                        <input type='file' name='csvFile' accept='.csv' required />
                        <button type='submit' style='padding: 5px 15px; background: #007bff; color: white; border: none; border-radius: 4px;'>Bắt đầu Bơm dữ liệu</button>
                    </form>
                </div>";
            return Content(html, "text/html", System.Text.Encoding.UTF8);
        }

        [HttpPost]
        public IActionResult ImportLocations(IFormFile csvFile)
        {
            if (csvFile == null || csvFile.Length == 0) return Content("Vui lòng chọn file CSV!");

            var existingDistricts = _context.Districts.Select(d => d.DistrictId).ToHashSet();
            var existingWards = _context.Wards.Select(w => w.WardId).ToHashSet();

            var newDistricts = new Dictionary<int, District>();
            var newWards = new List<Ward>();

            using (var reader = new StreamReader(csvFile.OpenReadStream()))
            {
                reader.ReadLine(); // Bỏ qua Header

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Chỉ cắt bằng đúng dấu phẩy thuần túy của CSV
                    var values = line.Split(',');

                    if (values.Length >= 9)
                    {
                        int len = values.Length;

                        // TUYỆT CHIÊU: Đọc từ 2 đầu mảng để "bóp chết" mọi lỗi do dư dấu phẩy ở giữa
                        string strWardId = values[0].Trim('"');
                        string strWardName = values[1].Trim('"');

                        string strProvinceId = values[len - 4].Trim('"');
                        string strDistrictId = values[len - 3].Trim('"');
                        string strDistrictName = values[len - 2].Trim('"');

                        // Nếu ép kiểu thành số thành công thì mới lấy
                        if (int.TryParse(strWardId, out int wardId) &&
                            int.TryParse(strProvinceId, out int provinceId) &&
                            int.TryParse(strDistrictId, out int districtId))
                        {
                            // 1. Gom Quận/Huyện mới
                            if (!existingDistricts.Contains(districtId) && !newDistricts.ContainsKey(districtId))
                            {
                                newDistricts.Add(districtId, new District
                                {
                                    DistrictId = districtId,
                                    ProvinceId = provinceId,
                                    DistrictName = strDistrictName
                                });
                            }

                            // 2. Gom Xã/Phường mới
                            if (!existingWards.Contains(wardId))
                            {
                                newWards.Add(new Ward
                                {
                                    WardId = wardId,
                                    DistrictId = districtId,
                                    WardName = strWardName
                                });
                                existingWards.Add(wardId); // Ghi nhận ngay để không bị trùng nội bộ
                            }
                        }
                    }
                }
            }

            // Đẩy tất cả vào DB cùng 1 lúc
            if (newDistricts.Any())
            {
                _context.Districts.AddRange(newDistricts.Values);
                _context.SaveChanges();
            }

            if (newWards.Any())
            {
                _context.Wards.AddRange(newWards);
                _context.SaveChanges();
            }

            return Content($"<h2 style='color: green; margin: 50px; font-family: Arial;'>Thành công rực rỡ! 🎉</h2><p style='margin-left: 50px; font-family: Arial;'>Đã Import thêm <b>{newDistricts.Count}</b> Quận/Huyện và <b>{newWards.Count}</b> Xã/Phường vào CSDL.</p>", "text/html", System.Text.Encoding.UTF8);
        }
    }
}