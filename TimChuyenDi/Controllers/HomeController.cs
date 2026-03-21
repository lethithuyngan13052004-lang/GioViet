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
        // 3. TOOL BƠM DỮ LIỆU TỐI THƯỢNG
        // ==================================================

        // HÀM HỖ TRỢ: Băm file CSV chuẩn, không sợ dấu phẩy ẩn
        private string[] ParseCsvRow(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();
            char separator = line.Contains(",") ? ',' : ';';

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == separator && !inQuotes)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString().Trim());
            return result.ToArray();
        }

        [HttpGet]
        public IActionResult ImportLocations()
        {
            string html = @"
                <div style='margin: 50px; font-family: Arial;'>
                    <h2>Tool Import 10.000+ Tỉnh/Huyện/Xã Toàn Quốc</h2>
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
            try
            {
                if (csvFile == null || csvFile.Length == 0) return Content("Vui lòng chọn file CSV!");

                // VŨ KHÍ 1: Vừa vào là "Tẩy não" EF Core ngay lập tức
                _context.ChangeTracker.Clear();

                // VŨ KHÍ 2: Dùng AsNoTracking() để cấm EF Core lưu ngầm vào bộ nhớ
                var existingProvinces = _context.Provinces.AsNoTracking().Select(p => p.ProvinceId).ToHashSet();
                var existingDistricts = _context.Districts.AsNoTracking().Select(d => d.DistrictId).ToHashSet();
                var existingWards = _context.Wards.AsNoTracking().Select(w => w.WardId).ToHashSet();

                var newProvinces = new Dictionary<int, Province>();
                var newDistricts = new Dictionary<int, District>();
                var newWards = new List<Ward>();

                var districtToCsvProvinceMap = new Dictionary<District, int>();
                var wardToCsvDistrictMap = new Dictionary<Ward, int>();

                int rowCount = 0;
                int errorCount = 0;

                using (var reader = new StreamReader(csvFile.OpenReadStream()))
                {
                    reader.ReadLine();

                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        rowCount++;

                        var values = ParseCsvRow(line);

                        if (values.Length >= 9)
                        {
                            string strWardId = values[0];
                            string strWardName = values[1];
                            string strProvinceId = values[5];
                            string strDistrictId = values[6];
                            string strDistrictName = values[7];
                            string strProvinceName = values[8];

                            if (int.TryParse(strWardId, out int wardId) &&
                                int.TryParse(strProvinceId, out int provinceId) &&
                                int.TryParse(strDistrictId, out int districtId))
                            {
                                if (!existingProvinces.Contains(provinceId) && !newProvinces.ContainsKey(provinceId))
                                {
                                    newProvinces.Add(provinceId, new Province { ProvinceId = provinceId, ProvinceName = strProvinceName });
                                }

                                if (!existingDistricts.Contains(districtId) && !newDistricts.ContainsKey(districtId))
                                {
                                    var newDist = new District { DistrictId = districtId, ProvinceId = provinceId, DistrictName = strDistrictName };
                                    newDistricts.Add(districtId, newDist);
                                    districtToCsvProvinceMap[newDist] = provinceId;
                                }

                                if (!existingWards.Contains(wardId))
                                {
                                    var newWard = new Ward { WardId = wardId, DistrictId = districtId, WardName = strWardName };
                                    newWards.Add(newWard);
                                    wardToCsvDistrictMap[newWard] = districtId;
                                    existingWards.Add(wardId);
                                }
                            }
                            else { errorCount++; }
                        }
                        else { errorCount++; }
                    }
                }

                // ========================================================
                // VŨ KHÍ 3: LƯU ĐẾN ĐÂU, QUÊN ĐẾN ĐÓ
                // ========================================================

                if (newProvinces.Any())
                {
                    _context.Provinces.AddRange(newProvinces.Values);
                    _context.SaveChanges();
                    _context.ChangeTracker.Clear(); // Lưu xong xoá trí nhớ
                }

                foreach (var dist in newDistricts.Values)
                {
                    if (newProvinces.TryGetValue(districtToCsvProvinceMap[dist], out var savedProv))
                    {
                        dist.ProvinceId = savedProv.ProvinceId;
                    }
                }

                if (newDistricts.Any())
                {
                    _context.Districts.AddRange(newDistricts.Values);
                    _context.SaveChanges();
                    _context.ChangeTracker.Clear(); // Lưu xong xoá trí nhớ
                }

                foreach (var ward in newWards)
                {
                    if (newDistricts.TryGetValue(wardToCsvDistrictMap[ward], out var savedDist))
                    {
                        ward.DistrictId = savedDist.DistrictId;
                    }
                }

                if (newWards.Any())
                {
                    _context.Wards.AddRange(newWards);
                    _context.SaveChanges();
                    _context.ChangeTracker.Clear(); // Lưu xong xoá trí nhớ
                }

                return Content($@"
                    <div style='margin: 50px; font-family: Arial;'>
                        <h2 style='color: green;'>Thành công rực rỡ! 🎉</h2>
                        <ul style='font-size: 18px;'>
                            <li>Đã quét: <b>{rowCount}</b> dòng</li>
                            <li>Bị lỗi format: <b><span style='color:red;'>{errorCount}</span></b> dòng</li>
                            <li>Đã thêm MỚI: <b>{newProvinces.Count}</b> Tỉnh, <b>{newDistricts.Count}</b> Huyện, <b>{newWards.Count}</b> Xã</li>
                        </ul>
                    </div>", "text/html", System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                string errorMsg = ex.InnerException?.Message ?? ex.Message;
                return Content($"<h2 style='color: red; margin: 50px; font-family: Arial;'>❌ Lỗi:</h2><p style='margin-left: 50px; font-family: Arial;'>{errorMsg}</p>", "text/html", System.Text.Encoding.UTF8);
            }
        }
        // ==================================================
        // TRANG TEST BẢN ĐỒ (LEAFLET + OPENSTREETMAP)
        // ==================================================
        [HttpGet]
        public IActionResult Map()
        {
            return View();
        }
    } // Đóng class HomeController

} // Đóng namespace TimChuyenDi.Controllers