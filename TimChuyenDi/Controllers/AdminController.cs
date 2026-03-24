using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using TimChuyenDi.Models;

namespace TimChuyenDi.Controllers
{
    [Authorize(Roles = "1")] // Số 1 là Role của Admin mà chúng ta đã định nghĩa
    public class AdminController : Controller
    {
        private readonly TimchuyendiContext _context;

        public AdminController(TimchuyendiContext context)
        {
            _context = context;
        }

        // GET: Quản lý danh sách User (có kèm tìm kiếm)
        public IActionResult Index(string searchPhone)
        {
            var query = _context.Users.AsQueryable();

            // Tính năng tìm kiếm: Lọc theo số điện thoại nếu Admin có nhập
            if (!string.IsNullOrEmpty(searchPhone))
            {
                query = query.Where(u => u.Phone.Contains(searchPhone));
            }

            // Sắp xếp người mới đăng ký lên đầu
            var users = query.OrderByDescending(u => u.UserId).ToList();

            // Giữ lại từ khóa tìm kiếm trên giao diện
            ViewBag.SearchPhone = searchPhone;

            return View(users);
        }

        // POST: Xử lý Khóa / Mở khóa tài khoản
        [HttpPost]
        public IActionResult ToggleStatus(int userId)
        {
            var user = _context.Users.Find(userId);

            // Kiểm tra user có tồn tại và KHÔNG PHẢI là Admin (tránh tự khóa mình)
            if (user != null && user.Role != 0)
            {
                // Đảo ngược trạng thái (Đang 1 thành 0, đang 0 thành 1)
                user.IsActive = !user.IsActive;
                _context.SaveChanges();
            }

            return RedirectToAction("Index");
        }

        // GET: Quản lý toàn bộ chuyến xe
        public IActionResult ManageTrips()
        {
            var trips = _context.Trips
                .Include(t => t.Driver)
                .Include(t => t.Vehicle)
                .Include(t => t.FromStationNavigation)
                .Include(t => t.ToStationNavigation)
                .OrderByDescending(t => t.StartTime) // Chuyến xe mới nhất lên đầu
                .ToList();

            return View(trips);
        }

        // GET: Quản lý danh sách phương tiện
        public IActionResult ManageVehicles()
        {
            var vehicles = _context.Vehicles.OrderByDescending(v => v.VehicleId).ToList();
            return View(vehicles);
        }

        // POST: Xử lý thêm xe mới
        [HttpPost]
        public IActionResult AddVehicle(string plateNumber, int maxCapacityKg)
        {
            // Kiểm tra dữ liệu đầu vào không được rỗng và tải trọng phải > 0
            if (!string.IsNullOrEmpty(plateNumber) && maxCapacityKg > 0)
            {
                // Kiểm tra xem biển số đã tồn tại chưa để tránh trùng lặp
                var exists = _context.Vehicles.Any(v => v.PlateNumber == plateNumber);
                if (!exists)
                {
                    var newVehicle = new Vehicle
                    {
                        PlateNumber = plateNumber,
                        CapacityKg = maxCapacityKg
                    };
                    _context.Vehicles.Add(newVehicle);
                    _context.SaveChanges();
                }
            }
            return RedirectToAction("ManageVehicles");
        }

        // ==================================================
        // QUẢN LÝ TRẠM (STATIONS) TRÊN BẢN ĐỒ
        // ==================================================

        // GET: Hiển thị giao diện bản đồ quản lý Trạm
        public IActionResult ManageStations()
        {
            // Truyền danh sách Tỉnh/TP để dùng cho form Thêm/Sửa trạm
            ViewBag.Provinces = _context.Provinces.OrderBy(p => p.ProvinceName).ToList();
            return View();
        }

        // API GET: Lấy danh sách tất cả các trạm để vẽ lên Map, hỗ trợ tìm kiếm
        [HttpGet]
        public IActionResult GetStations(string searchName = null, int? provinceId = null, int? districtId = null, int? wardId = null)
        {
            var query = _context.Stations
                .Include(s => s.Province)
                .Include(s => s.District)
                .Include(s => s.Ward)
                .AsQueryable();

            if (provinceId.HasValue && provinceId.Value > 0)
            {
                query = query.Where(s => s.ProvinceId == provinceId.Value);
            }

            if (districtId.HasValue && districtId.Value > 0)
            {
                query = query.Where(s => s.DistrictId == districtId.Value);
            }

            if (wardId.HasValue && wardId.Value > 0)
            {
                query = query.Where(s => s.WardId == wardId.Value);
            }

            var stationsList = query.ToList();

            if (!string.IsNullOrEmpty(searchName))
            {
                var normalizedSearch = NormalizeAddressName(searchName);
                stationsList = stationsList.Where(s => 
                    (s.StationName != null && NormalizeAddressName(s.StationName).Contains(normalizedSearch)) || 
                    (s.StationName != null && s.StationName.ToLower().Contains(searchName.ToLower())) ||
                    (s.Address != null && NormalizeAddressName(s.Address).Contains(normalizedSearch))
                ).ToList();
            }

            var stations = stationsList.Select(s => new
            {
                id = s.StationId,
                name = s.StationName,
                address = s.Address,
                lat = s.Latitude,
                lng = s.Longitude,
                provinceId = s.ProvinceId,
                districtId = s.DistrictId,
                wardId = s.WardId,
                provinceName = s.Province != null ? s.Province.ProvinceName : "",
                districtName = s.District != null ? s.District.DistrictName : "",
                wardName = s.Ward != null ? s.Ward.WardName : ""
            }).ToList();

            return Json(stations);
        }

        // API POST: Thêm mới hoặc Cập nhật Trạm
        [HttpPost]
        public IActionResult SaveStation([FromBody] TimChuyenDi.Models.Station model)
        {
            try
            {
                if (model.StationId == 0) // Thêm mới
                {
                    _context.Stations.Add(model);
                }
                else // Cập nhật
                {
                    var existing = _context.Stations.Find(model.StationId);
                    if (existing == null) return NotFound("Không tìm thấy trạm này");

                    existing.StationName = model.StationName;
                    existing.Address = model.Address;
                    existing.Latitude = model.Latitude;
                    existing.Longitude = model.Longitude;
                    existing.ProvinceId = model.ProvinceId;
                    existing.DistrictId = model.DistrictId;
                    existing.WardId = model.WardId;
                }

                _context.SaveChanges();
                return Ok(new { success = true, message = "Lưu trạm thành công" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        // API POST: Xoá trạm
        [HttpPost]
        public IActionResult DeleteStation(int id)
        {
            var station = _context.Stations.Find(id);
            if (station != null)
            {
                // Kiểm tra xem trạm có đang được sử dụng trong Trip nào không trước khi xoá (tuỳ chọn)
                bool isInUse = _context.Trips.Any(t => t.FromStation == id || t.ToStation == id);
                if (isInUse)
                {
                    return BadRequest(new { success = false, message = "Không thể xoá vì trạm này đang được sử dụng trong chuyến xe." });
                }

                _context.Stations.Remove(station);
                _context.SaveChanges();
                return Ok(new { success = true });
            }
            return NotFound(new { success = false, message = "Không tìm thấy trạm." });
        }

        // Helper method to normalize names for comparison
        private string NormalizeAddressName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            name = name.ToLower().Trim();

            // Remove common prefixes
            string[] prefixes = { "thành phố", "tỉnh", "quận", "huyện", "thị xã", "phường", "xã", "thị trấn" };
            foreach (var prefix in prefixes)
            {
                if (name.StartsWith(prefix + " "))
                {
                    name = name.Substring(prefix.Length).Trim();
                }
                else
                {
                    name = name.Replace(prefix, "").Trim();
                }
            }

            // Remove diacritics
            string[] VietnameseSigns = {
                "aAeEoOuUiIdDyY",
                "áàạảãâấầậẩẫăắằặẳẵ",
                "ÁÀẠẢÃÂẤẦẬẨẪĂẮẰẶẲẴ",
                "éèẹẻẽêếềệểễ",
                "ÉÈẸẺẼÊẾỀỆỂỄ",
                "óòọỏõôốồộổỗơớờợởỡ",
                "ÓÒỌỎÕÔỐỒỘỔỖƠỚỜỢỞỠ",
                "úùụủũưứừựửữ",
                "ÚÙỤỦŨƯỨỪỰỬỮ",
                "íìịỉĩ",
                "ÍÌỊỈĨ",
                "đ",
                "Đ",
                "ýỳỵỷỹ",
                "ÝỲỴỶỸ"
            };

            for (int i = 1; i < VietnameseSigns.Length; i++)
            {
                for (int j = 0; j < VietnameseSigns[i].Length; j++)
                {
                    name = name.Replace(VietnameseSigns[i][j].ToString(), VietnameseSigns[0][i - 1].ToString());
                }
            }

            return name.Trim();
        }

        // API GET: Map API response (chuỗi địa chỉ) về ID của CSDL
        [HttpGet]
        public IActionResult ResolveLocationIds(string provinceName, string districtName, string wardName)
        {
            int? pId = null, dId = null, wId = null;

            if (!string.IsNullOrEmpty(provinceName))
            {
                var pSearch = NormalizeAddressName(provinceName);
                var provinces = _context.Provinces.ToList();
                var province = provinces.FirstOrDefault(p => NormalizeAddressName(p.ProvinceName).Contains(pSearch) || pSearch.Contains(NormalizeAddressName(p.ProvinceName)));
                
                if (province != null)
                {
                    pId = province.ProvinceId;

                    if (!string.IsNullOrEmpty(districtName))
                    {
                        var dSearch = NormalizeAddressName(districtName);
                        var districts = _context.Districts.Where(d => d.ProvinceId == pId).ToList();
                        var district = districts.FirstOrDefault(d => NormalizeAddressName(d.DistrictName).Contains(dSearch) || dSearch.Contains(NormalizeAddressName(d.DistrictName)));
                        
                        if (district != null)
                        {
                            dId = district.DistrictId;

                            if (!string.IsNullOrEmpty(wardName))
                            {
                                var wSearch = NormalizeAddressName(wardName);
                                var wards = _context.Wards.Where(w => w.DistrictId == dId).ToList();
                                var ward = wards.FirstOrDefault(w => NormalizeAddressName(w.WardName).Contains(wSearch) || wSearch.Contains(NormalizeAddressName(w.WardName)));
                                
                                if (ward != null)
                                {
                                    wId = ward.WardId;
                                }
                            }
                        }
                    }
                }
            }

            return Json(new { provinceId = pId, districtId = dId, wardId = wId });
        }
    }
}