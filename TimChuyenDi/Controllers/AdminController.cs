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
        public IActionResult ToggleStatus(int userId, string? lockReason)
        {
            if (userId == 1)
            {
                TempData["ErrorMessage"] = "Không thể khoá tài khoản Admin tối cao!";
                return RedirectToAction("Index");
            }

            var user = _context.Users.Find(userId);

            // Kiểm tra user có tồn tại và KHÔNG PHẢI là Admin (tránh tự khóa mình, role = 0)
            if (user != null && user.Role != 0)
            {
                if (user.IsActive == true)
                {
                    // Khóa
                    user.IsActive = false;
                    user.LockReason = string.IsNullOrEmpty(lockReason) ? "Vi phạm chính sách hệ thống" : lockReason;
                    TempData["SuccessMessage"] = "Đã khoá tài khoản thành công.";
                }
                else
                {
                    // Mở khóa
                    user.IsActive = true;
                    user.LockReason = null;
                    TempData["SuccessMessage"] = "Đã mở khoá tài khoản thành công.";
                }
                
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
                    .ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation)
                    .ThenInclude(s => s.Province)
                .OrderByDescending(t => t.StartTime) // Chuyến xe mới nhất lên đầu
                .ToList();

            return View(trips);
        }

        // GET: Quản lý danh sách phương tiện
        public IActionResult ManageVehicles()
        {
            var vehicles = _context.Vehicles
                .Include(v => v.Driver)
                .Include(v => v.VehicleType)
                .OrderBy(v => v.Status) // Chờ duyệt (0) lên trước
                .ThenByDescending(v => v.VehicleId)
                .ToList();
            return View(vehicles);
        }

        // POST: Duyệt phương tiện
        [HttpPost]
        public IActionResult ApproveVehicle(int id)
        {
            var vehicle = _context.Vehicles.Find(id);
            if (vehicle != null && vehicle.Status != 1)
            {
                vehicle.Status = 1; // 1 = Đã duyệt
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Đã duyệt xe biển số {vehicle.PlateNumber} thành công!";
            }
            return RedirectToAction("ManageVehicles");
        }

        // POST: Từ chối phương tiện
        [HttpPost]
        public IActionResult RejectVehicle(int id)
        {
            var vehicle = _context.Vehicles.Find(id);
            if (vehicle != null && vehicle.Status != 2)
            {
                vehicle.Status = 2; // 2 = Từ chối
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Đã từ chối xe biển số {vehicle.PlateNumber}!";
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

        // ==================================================
        // QUẢN LÝ CẤU HÌNH HỆ THỐNG (loại hàng, loại xe, hệ số)
        // ==================================================
        public IActionResult ManageSettings()
        {
            ViewBag.CargoTypes = _context.Cargotypes.OrderBy(c => c.CargoTypeId).ToList();
            ViewBag.VehicleTypes = _context.VehicleTypes.OrderBy(v => v.VehicleTypeId).ToList();
            ViewBag.TripTypes = _context.TripTypes.OrderBy(t => t.IdType).ToList();
            ViewBag.SystemConfigs = _context.SystemConfigs.ToList();
            return View();
        }

        // ---- LOẠI HÀNG (CargoType) ----
        [HttpPost]
        public IActionResult SaveCargoType(int CargoTypeId, string TypeName, decimal PriceMultiplier)
        {
            if (CargoTypeId == 0)
            {
                _context.Cargotypes.Add(new Cargotype { TypeName = TypeName, PriceMultiplier = PriceMultiplier });
                TempData["SuccessMessage"] = $"Đã thêm loại hàng \"{TypeName}\"";
            }
            else
            {
                var item = _context.Cargotypes.Find(CargoTypeId);
                if (item != null)
                {
                    item.TypeName = TypeName;
                    item.PriceMultiplier = PriceMultiplier;
                    TempData["SuccessMessage"] = $"Đã cập nhật loại hàng \"{TypeName}\"";
                }
            }
            _context.SaveChanges();
            return RedirectToAction("ManageSettings");
        }

        [HttpPost]
        public IActionResult DeleteCargoType(int id)
        {
            var item = _context.Cargotypes.Find(id);
            if (item != null)
            {
                _context.Cargotypes.Remove(item);
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Đã xoá loại hàng \"{item.TypeName}\"";
            }
            return RedirectToAction("ManageSettings");
        }

        // ---- LOẠI XE (VehicleType) ----
        [HttpPost]
        public IActionResult SaveVehicleType(int VehicleTypeId, string TypeName, string? Description)
        {
            if (VehicleTypeId == 0)
            {
                _context.VehicleTypes.Add(new VehicleType { TypeName = TypeName, Description = Description });
                TempData["SuccessMessage"] = $"Đã thêm loại xe \"{TypeName}\"";
            }
            else
            {
                var item = _context.VehicleTypes.Find(VehicleTypeId);
                if (item != null)
                {
                    item.TypeName = TypeName;
                    item.Description = Description;
                    TempData["SuccessMessage"] = $"Đã cập nhật loại xe \"{TypeName}\"";
                }
            }
            _context.SaveChanges();
            return RedirectToAction("ManageSettings");
        }

        [HttpPost]
        public IActionResult DeleteVehicleType(int id)
        {
            var item = _context.VehicleTypes.Find(id);
            if (item != null)
            {
                bool inUse = _context.Vehicles.Any(v => v.VehicleTypeId == id);
                if (inUse)
                {
                    TempData["ErrorMessage"] = "Không thể xoá vì có xe đang sử dụng loại xe này!";
                    return RedirectToAction("ManageSettings");
                }
                _context.VehicleTypes.Remove(item);
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Đã xoá loại xe \"{item.TypeName}\"";
            }
            return RedirectToAction("ManageSettings");
        }

        // ---- LOẠI CHUYẾN / HỆ SỐ (TripType) ----
        [HttpPost]
        public IActionResult SaveTripType(int IdType, string Type, decimal Multiplier)
        {
            if (IdType == 0)
            {
                _context.TripTypes.Add(new TripType { Type = Type, Multiplier = Multiplier });
                TempData["SuccessMessage"] = $"Đã thêm loại chuyến \"{Type}\"";
            }
            else
            {
                var item = _context.TripTypes.Find(IdType);
                if (item != null)
                {
                    item.Type = Type;
                    item.Multiplier = Multiplier;
                    TempData["SuccessMessage"] = $"Đã cập nhật loại chuyến \"{Type}\"";
                }
            }
            _context.SaveChanges();
            return RedirectToAction("ManageSettings");
        }

        [HttpPost]
        public IActionResult DeleteTripType(int id)
        {
            var item = _context.TripTypes.Find(id);
            if (item != null)
            {
                bool inUse = _context.Trips.Any(t => t.RouteType == id);
                if (inUse)
                {
                    TempData["ErrorMessage"] = "Không thể xoá vì có chuyến xe đang sử dụng loại chuyến này!";
                    return RedirectToAction("ManageSettings");
                }
                _context.TripTypes.Remove(item);
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Đã xoá loại chuyến \"{item.Type}\"";
            }
            return RedirectToAction("ManageSettings");
        }

        // ---- CẤU HÌNH HỆ THỐNG (SystemConfig) ----
        [HttpPost]
        public IActionResult SaveSystemConfig(string KeyName, decimal Value, bool isNew = false)
        {
            if (isNew)
            {
                if (_context.SystemConfigs.Any(c => c.KeyName == KeyName))
                {
                    TempData["ErrorMessage"] = "Key đã tồn tại!";
                    return RedirectToAction("ManageSettings");
                }
                _context.SystemConfigs.Add(new SystemConfig { KeyName = KeyName, Value = Value });
                TempData["SuccessMessage"] = $"Đã thêm cấu hình \"{KeyName}\"";
            }
            else
            {
                var item = _context.SystemConfigs.Find(KeyName);
                if (item != null)
                {
                    item.Value = Value;
                    TempData["SuccessMessage"] = $"Đã cập nhật cấu hình \"{KeyName}\" = {Value}";
                }
            }
            _context.SaveChanges();
            return RedirectToAction("ManageSettings");
        }

        [HttpPost]
        public IActionResult DeleteSystemConfig(string keyName)
        {
            var item = _context.SystemConfigs.Find(keyName);
            if (item != null)
            {
                _context.SystemConfigs.Remove(item);
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Đã xoá cấu hình \"{keyName}\"";
            }
            return RedirectToAction("ManageSettings");
        }

        [HttpGet]
        public IActionResult ConfirmChatConfig(string key, decimal val)
        {
            var item = _context.SystemConfigs.Find(key);
            if (item != null)
            {
                item.Value = val;
                _context.SaveChanges();
                TempData["SuccessMessage"] = $"Cập nhật cấu hình hệ thống: {key} = {val} thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = $"Không tìm thấy cấu hình mang tên: {key}";
            }
            return RedirectToAction("ManageSettings");
        }
    }
}