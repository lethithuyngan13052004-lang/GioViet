using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Collections.Generic;
using TimChuyenDi.Models;

namespace TimChuyenDi.Controllers
{
    [Authorize] // Bắt buộc đăng nhập
    public class DriverController : Controller
    {
        private readonly TimchuyendiContext _context;
        private readonly IWebHostEnvironment _env;

        public DriverController(TimchuyendiContext context, IWebHostEnvironment env)
        {
            _context = context;

            _env = env;
        }
        // GET: Trang chủ của Tài xế - Hiển thị danh sách khách gửi hàng
        public async Task<IActionResult> Index()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            // Nối bảng sâu: Đơn hàng -> Chuyến xe -> Trạm -> Tỉnh
            var requests = await _context.Shiprequests
                .Include(r => r.User)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.FromStationNavigation)
                        .ThenInclude(s => s.Province)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.ToStationNavigation)
                        .ThenInclude(s => s.Province)
                .Where(r => r.Trip.DriverId == driverId)
                .OrderByDescending(r => r.PickupTimeFrom)
                .ToListAsync();

            return View(requests);
        }

        // POST: Xử lý cập nhật trạng thái đơn hàng nâng cao
        [HttpPost]
        public async Task<IActionResult> UpdateRequestStatus(int reqId, int status)
        {
            var request = await _context.Shiprequests
                                  .Include(r => r.Trip)
                                  .Include(r => r.Cargodetails)
                                  .FirstOrDefaultAsync(r => r.Id == reqId);

            if (request != null)
            {
                // 1. Chờ xác nhận (0) -> Nhận đơn (1) hoặc Từ chối (2)
                if (request.Status == 0 && (status == 1 || status == 2))
                {
                    request.Status = status;

                    if (status == 1) // Nếu nhận đơn -> Trừ sức chứa của xe
                    {
                        request.Trip.AvaiCapacityKg -= (int)(request.Cargodetails.FirstOrDefault()?.Weight ?? 0);
                    }
                    await _context.SaveChangesAsync();
                }
                // 2. Đã xác nhận (1) -> Đang giao (3) HOẶC Đang giao (3) -> Hoàn tất (4)
                else if ((request.Status == 1 && status == 3) || (request.Status == 3 && status == 4))
                {
                    request.Status = status;
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToAction("Index");
        }

        // GET: Xem danh sách các chuyến xe tài xế đã đăng
        public async Task<IActionResult> MyTrips(int page = 1)
        {
            int pageSize = 9;
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);
            
            var query = _context.Trips
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.Vehicle)
                .Include(t => t.RouteTypeNavigation)
                .Where(t => t.DriverId == driverId && t.StartTime >= DateTime.Now)
                .OrderByDescending(t => t.StartTime);

            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            var trips = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(trips);
        }

        // GET: Hiển thị form đăng chuyến xe mới
        [HttpGet]
        public async Task<IActionResult> CreateTrip()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            // Danh sách trạm đầy đủ để tra cứu trên Map
            var stations = await _context.Stations
                .Include(s => s.Province)
                .OrderBy(s => s.StationName)
                .ToListAsync();
            
            ViewBag.StationsRaw = stations.Select(s => new {
                s.StationId,
                s.StationName,
                s.Latitude,
                s.Longitude,
                ProvinceName = s.Province.ProvinceName,
                s.Address
            }).ToList();

            // Loại chuyến đi (Radio Buttons)
            ViewBag.TripTypes = await _context.TripTypes.ToListAsync();

            // Danh sách Tỉnh/Thành
            ViewBag.Provinces = await _context.Provinces.OrderBy(p => p.ProvinceName).ToListAsync();

            // Xe của tài xế đã duyệt
            var myVehicles = await _context.Vehicles
                .Where(v => v.DriverId == driverId && v.Status == 1)
                .Include(v => v.VehicleType)
                .ToListAsync();
            ViewBag.Vehicles = myVehicles;

            return View();
        }

        [HttpGet]
        public IActionResult GetStationsByProvince(int? provinceId)
        {
            var query = _context.Stations.AsQueryable();
            if (provinceId.HasValue)
            {
                query = query.Where(s => s.ProvinceId == provinceId.Value);
            }

            var stations = query
                .Include(s => s.Province)
                .Include(s => s.District)
                .Include(s => s.Ward)
                .Select(s => new {
                    id = s.StationId,
                    name = s.StationName,
                    lat = s.Latitude,
                    lng = s.Longitude,
                    address = s.Address,
                    province = s.Province.ProvinceName,
                    provinceId = s.ProvinceId,
                    districtName = s.District != null ? s.District.DistrictName : "",
                    wardName = s.Ward != null ? s.Ward.WardName : ""
                }).ToList();
            return Json(stations);
        }

        [HttpGet]
        public IActionResult GetStationsApi()
        {
            var stations = _context.Stations
                .Include(s => s.Province)
                .Select(s => new {
                    id = s.StationId,
                    name = s.StationName,
                    lat = s.Latitude,
                    lng = s.Longitude,
                    address = s.Address,
                    province = s.Province.ProvinceName
                }).ToList();
            return Json(stations);
        }

        // POST: Xử lý lưu chuyến xe mới vào Database
        [HttpPost]
        public async Task<IActionResult> CreateTrip(Trip model, string intermediateStations)
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            // Kiểm tra xe có tồn tại và đã được duyệt (Status == 1) hay chưa
            var vehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleId == model.VehicleId && v.DriverId == driverId && v.Status == 1);

            if (vehicle == null)
            {
                TempData["Error"] = "Không tìm thấy xe hợp lệ hoặc xe của bạn chưa được Admin phê duyệt. Vui lòng kiểm tra lại!";
                return RedirectToAction("CreateTrip");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                model.DriverId = driverId;
                // Mặc định Capacity nếu tài xế không sửa
                if (model.AvaiCapacityKg <= 0) model.AvaiCapacityKg = vehicle.CapacityKg;

                _context.Trips.Add(model);
                // Save lần đầu để có Identity Id cho TripId
                await _context.SaveChangesAsync();

                // Xử lý trạm trung gian (Dạng JSON)
                if (!string.IsNullOrEmpty(intermediateStations))
                {
                    try 
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var stops = JsonSerializer.Deserialize<List<IntermediateStationDto>>(intermediateStations, options);
                        if (stops != null)
                        {
                            for (int i = 0; i < stops.Count; i++)
                            {
                                var ts = new TripStation
                                {
                                    TripId = model.TripId,
                                    StationId = stops[i].stationId,
                                    StopOrder = i + 1,
                                    DistanceFromPrev = stops[i].distance,
                                    EstArrivalTime = stops[i].estArrivalTime
                                };
                                _context.TripStations.Add(ts);
                            }
                            // Save lần 2 cho các trạm trung gian
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("JSON Parse Error: " + ex.Message);
                    }
                }

                await transaction.CommitAsync();
                TempData["Success"] = "Đăng chuyến xe và lộ trình thành công!";
                return RedirectToAction("MyTrips");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "Lỗi hệ thống khi lưu chuyến xe: " + ex.Message;
                return RedirectToAction("CreateTrip");
            }
        }

        // GET: Xem các đánh giá của khách hàng về tài xế
        [HttpGet]
        public async Task<IActionResult> MyReviews()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            // Logic mới: Đánh giá nối vào Đơn hàng (Req), Đơn hàng nối vào Chuyến xe (Trip)
            var reviews = await _context.Ratings
                .Include(r => r.Customer)
                .Include(r => r.Req)
                    .ThenInclude(req => req.Trip)
                        .ThenInclude(t => t.FromStationNavigation)
                            .ThenInclude(s => s.Province)
                .Include(r => r.Req)
                    .ThenInclude(req => req.Trip)
                        .ThenInclude(t => t.ToStationNavigation)
                            .ThenInclude(s => s.Province)
                .Where(r => r.Req.Trip.DriverId == driverId)
                .OrderByDescending(r => r.RatingId)
                .ToListAsync();

            ViewBag.AverageScore = reviews.Any() ? Math.Round(reviews.Average(r => r.Score), 1) : 0;
            ViewBag.TotalReviews = reviews.Count;

            return View(reviews);
        }

        // GET: Hiển thị form Sửa chuyến xe
        [HttpGet]
        public async Task<IActionResult> EditTrip(int id)
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            var trip = await _context.Trips
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.TripStations).ThenInclude(ts => ts.Station).ThenInclude(s => s.Province)
                .FirstOrDefaultAsync(t => t.TripId == id && t.DriverId == driverId);

            if (trip == null) return NotFound();

            // All necessary ViewBag data (copied from CreateTrip)
            var stations = await _context.Stations
                .Include(s => s.Province)
                .OrderBy(s => s.StationName)
                .ToListAsync();

            ViewBag.StationsRaw = stations.Select(s => new {
                s.StationId,
                s.StationName,
                s.Latitude,
                s.Longitude,
                ProvinceName = s.Province.ProvinceName,
                s.Address
            }).ToList();

            ViewBag.TripTypes = await _context.TripTypes.ToListAsync();
            ViewBag.Provinces = await _context.Provinces.OrderBy(p => p.ProvinceName).ToListAsync();
            ViewBag.Vehicles = await _context.Vehicles
                .Where(v => v.DriverId == driverId && v.Status == 1)
                .Include(v => v.VehicleType)
                .ToListAsync();

            return View(trip);
        }

        // POST: Xử lý lưu thông tin Sửa chuyến xe
        [HttpPost]
        public async Task<IActionResult> EditTrip(Trip updatedTrip, string intermediateStations)
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            var trip = await _context.Trips
                .Include(t => t.TripStations)
                .FirstOrDefaultAsync(t => t.TripId == updatedTrip.TripId && t.DriverId == driverId);

            if (trip == null) return NotFound();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Update basic info
                trip.VehicleId = updatedTrip.VehicleId;
                trip.RouteType = updatedTrip.RouteType;
                trip.FromStation = updatedTrip.FromStation;
                trip.ToStation = updatedTrip.ToStation;
                trip.StartTime = updatedTrip.StartTime;
                trip.ArrivalTime = updatedTrip.ArrivalTime;
                trip.AvaiCapacityKg = updatedTrip.AvaiCapacityKg;
                trip.BasePrice = updatedTrip.BasePrice;
                trip.Distance = updatedTrip.Distance;

                // Sync Intermediate Stations (Clear and rebuild)
                _context.TripStations.RemoveRange(trip.TripStations);
                
                if (!string.IsNullOrEmpty(intermediateStations))
                {
                    try 
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var stops = JsonSerializer.Deserialize<List<IntermediateStationDto>>(intermediateStations, options);
                        if (stops != null)
                        {
                            for (int i = 0; i < stops.Count; i++)
                            {
                                var ts = new TripStation
                                {
                                    TripId = trip.TripId,
                                    StationId = stops[i].stationId,
                                    StopOrder = i + 1,
                                    DistanceFromPrev = stops[i].distance,
                                    EstArrivalTime = stops[i].estArrivalTime
                                };
                                _context.TripStations.Add(ts);
                            }
                        }
                    }
                    catch (Exception jsonEx)
                    {
                        System.Diagnostics.Debug.WriteLine("JSON Parse Error in Edit: " + jsonEx.Message);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                TempData["Success"] = "Cập nhật chuyến xe và lộ trình thành công!";
                return RedirectToAction("MyTrips");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "Lỗi khi cập nhật chuyến xe: " + ex.Message;
                return RedirectToAction("EditTrip", new { id = updatedTrip.TripId });
            }
        }

        // POST: Xử lý Xóa chuyến xe
        [HttpPost]
        public async Task<IActionResult> DeleteTrip(int id)
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            var trip = await _context.Trips
                               .Include(t => t.Shiprequests)
                               .Include(t => t.Users)
                               .FirstOrDefaultAsync(t => t.TripId == id && t.DriverId == driverId);

            if (trip != null)
            {
                if (trip.Shiprequests.Any())
                {
                    TempData["Error"] = "Không thể xóa vì đã có khách hàng đặt chỗ. Vui lòng từ chối đơn trước!";
                    return RedirectToAction("MyTrips");
                }

                // Xóa quan hệ saved routes (many-to-many)
                trip.Users.Clear();

                _context.Trips.Remove(trip);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Xóa chuyến xe thành công!";
            }

            return RedirectToAction("MyTrips");
        }

        // GET: Xem chi tiết 1 yêu cầu gửi hàng
        [HttpGet]
        public IActionResult RequestDetail(int id)
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));

            var requestDetail = _context.Shiprequests
                .Include(r => r.User)
                .Include(r => r.Cargodetails)
                .Include(r => r.Shippingroutes)
                    .ThenInclude(sr => sr.FromStation)
                .Include(r => r.Shippingroutes)
                    .ThenInclude(sr => sr.ToStation)

                .Include(r => r.Trip)
                    .ThenInclude(t => t.FromStationNavigation)
                        .ThenInclude(s => s.Province)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.ToStationNavigation)
                        .ThenInclude(s => s.Province)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.Vehicle)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.TripStations)
                        .ThenInclude(ts => ts.Station)
                .FirstOrDefault(r => r.Id == id && r.Trip.DriverId == driverId);

            if (requestDetail == null) return NotFound("Không tìm thấy đơn hàng!");

            return View(requestDetail);
        }

        // ==================================================
        // QUẢN LÝ XE CỦA TÀI XẾ
        // ==================================================

        // GET: Danh sách xe của tài xế
        [HttpGet]
        public async Task<IActionResult> ManageVehicles()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            var vehicles = await _context.Vehicles
                .Include(v => v.VehicleType)
                .Where(v => v.DriverId == driverId)
                .OrderByDescending(v => v.VehicleId)
                .ToListAsync();

            return View(vehicles);
        }

        // GET: Form Thêm xe mới
        [HttpGet]
        public IActionResult AddVehicle()
        {
            ViewBag.VehicleTypes = new SelectList(_context.VehicleTypes.ToList(), "VehicleTypeId", "TypeName");
            return View();
        }

        // POST: Lưu thông tin xe mới
        [HttpPost, ActionName("AddVehicle")]
        public async Task<IActionResult> AddVehiclePost()
        {
            try 
            {
                var PlateNumber = Request.Form["PlateNumber"].ToString();
                int CapacityKg = int.Parse(Request.Form["CapacityKg"].ToString());
                int VehicleTypeId = int.Parse(Request.Form["VehicleTypeId"].ToString());
                var imageFile = Request.Form.Files.FirstOrDefault(f => f.Name == "imageFile");

                var driverId = int.Parse(User.FindFirstValue("UserId"));
                
                // Kiểm tra trùng lặp biển số
                if (_context.Vehicles.Any(v => v.PlateNumber == PlateNumber))
                {
                    TempData["Error"] = $"Biển số xe {PlateNumber} đã tồn tại trong hệ thống. Vui lòng kiểm tra lại!";
                    return RedirectToAction("ManageVehicles");
                }

                var model = new Vehicle 
                {
                    DriverId = driverId,
                    Status = 0, // Chờ duyệt
                    PlateNumber = PlateNumber,
                    CapacityKg = CapacityKg,
                    VehicleTypeId = VehicleTypeId
                };

                if (imageFile != null && imageFile.Length > 0)
                {
                    // Ensure directory exists
                    string uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "vehicles");
                    if (!Directory.Exists(uploadFolder))
                    {
                        Directory.CreateDirectory(uploadFolder);
                    }

                    // Generate unique file name
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(imageFile.FileName);
                    string filePath = Path.Combine(uploadFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(fileStream);
                    }

                    model.VehicleImage = "/uploads/vehicles/" + uniqueFileName;
                }

                var capacityConfig = await _context.VehicleCapacityConfigs
                    .FirstOrDefaultAsync(c => c.VehicleTypeId == VehicleTypeId 
                                           && CapacityKg >= c.MinWeight 
                                           && CapacityKg <= c.MaxWeight);
                                           

                _context.Vehicles.Add(model);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Thêm xe thành công. Đang chờ Admin duyệt!";
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? " - " + ex.InnerException.Message : "";
                TempData["Error"] = $"Hệ thống gặp lỗi: {ex.Message}{innerMsg}";
            }
            return RedirectToAction("ManageVehicles");
        }

        // GET: Form Sửa thông tin xe
        [HttpGet]
        public async Task<IActionResult> EditVehicle(int id)
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            var vehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleId == id && v.DriverId == driverId);

            if (vehicle == null) return NotFound();

            ViewBag.VehicleTypes = new SelectList(await _context.VehicleTypes.ToListAsync(), "VehicleTypeId", "TypeName");
            return View(vehicle);
        }

        // POST: Lưu thông tin sửa xe
        [HttpPost, ActionName("EditVehicle")]
        public async Task<IActionResult> EditVehiclePost()
        {
            try
            {
                int VehicleId = int.Parse(Request.Form["VehicleId"].ToString());
                string PlateNumber = Request.Form["PlateNumber"].ToString();
                int CapacityKg = int.Parse(Request.Form["CapacityKg"].ToString());
                int VehicleTypeId = int.Parse(Request.Form["VehicleTypeId"].ToString());
                var imageFile = Request.Form.Files.FirstOrDefault(f => f.Name == "imageFile");

                var userIdStr = User.FindFirstValue("UserId");
                if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
                int driverId = int.Parse(userIdStr);
                
                // Kiểm tra trùng lặp biển số với xe khác
                if (await _context.Vehicles.AnyAsync(v => v.PlateNumber == PlateNumber && v.VehicleId != VehicleId))
                {
                    TempData["Error"] = $"Biển số xe {PlateNumber} đã tồn tại ở một xe khác. Vui lòng kiểm tra lại!";
                    return RedirectToAction("ManageVehicles");
                }

                var vehicle = await _context.Vehicles
                    .FirstOrDefaultAsync(v => v.VehicleId == VehicleId && v.DriverId == driverId);

                if (vehicle != null)
                {
                    vehicle.PlateNumber = PlateNumber;
                    vehicle.CapacityKg = CapacityKg;
                    vehicle.VehicleTypeId = VehicleTypeId;
                    vehicle.Status = 0; // Sửa xong lại chờ duyệt

                    if (imageFile != null && imageFile.Length > 0)
                    {
                        string uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "vehicles");
                        if (!Directory.Exists(uploadFolder)) Directory.CreateDirectory(uploadFolder);

                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(imageFile.FileName);
                        string filePath = Path.Combine(uploadFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(fileStream);
                        }

                        // Xóa file cũ
                        if (!string.IsNullOrEmpty(vehicle.VehicleImage))
                        {
                            var oldFilePath = Path.Combine(_env.WebRootPath, vehicle.VehicleImage.TrimStart('/'));
                            if (System.IO.File.Exists(oldFilePath))
                            {
                                System.IO.File.Delete(oldFilePath);
                            }
                        }

                        vehicle.VehicleImage = "/uploads/vehicles/" + uniqueFileName;
                    }

                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Cập nhật thông tin xe thành công. Xe của bạn cần được Admin duyệt lại!";
                }
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? " - " + ex.InnerException.Message : "";
                TempData["Error"] = "Lỗi hệ thống: " + ex.Message + innerMsg;
            }
            return RedirectToAction("ManageVehicles");
        }

        // GET: Hiển thị các đơn hàng chờ ghép (chưa có chuyến) có lộ trình phù hợp với tài xế
        [HttpGet]
        public async Task<IActionResult> AvailableOrders()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            // BƯỚC 1: Lấy danh sách các chuyến xe sắp chạy của tài xế này
            var activeTrips = await _context.Trips
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Where(t => t.DriverId == driverId && t.StartTime > DateTime.Now)
                .ToListAsync();


            if (!activeTrips.Any())
            {
                ViewBag.Message = "Bạn chưa có chuyến xe nào sắp khởi hành. Vui lòng đăng chuyến xe trước để tìm đơn ghép!";
                return View(new List<Shiprequest>());
            }

            // Lấy danh sách các cặp Tỉnh đi - Tỉnh đến mà tài xế đang chạy
            var activeRoutes = activeTrips.Select(t => new { From = t.FromStationNavigation.ProvinceId, To = t.ToStationNavigation.ProvinceId }).Distinct().ToList();

            // BƯỚC 2: Tìm các đơn hàng chưa có chuyến (TripId == null) và khớp lộ trình
            var availableRequests = await _context.Shiprequests
                .Include(r => r.User)
                .Include(r => r.Cargodetails)
                .Include(r => r.Shippingroutes)
                    .ThenInclude(sr => sr.FromStation)
                .Include(r => r.Shippingroutes)
                    .ThenInclude(sr => sr.ToStation)
                .Where(r => r.TripId == null && (r.Status == 0 || r.Status == null))
                .ToListAsync(); // Thực hiện lọc nốt phía Client nếu logic route phức tạp
                
            var filteredRequests = availableRequests.Where(r => {
                    var route = r.Shippingroutes.FirstOrDefault();
                    return route != null && activeRoutes.Any(ar => ar.From == route.FromProvinceId && ar.To == route.ToProvinceId);
                })
                .OrderBy(r => r.PickupTimeTo ?? DateTime.MaxValue) // Ưu tiên hạn giao dự kiến
                .ToList();


            ViewBag.ActiveTrips = activeTrips;
            return View(filteredRequests);
        }

        // POST: Xóa xe
        [HttpPost]
        public async Task<IActionResult> DeleteVehicle(int id)
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            var vehicle = await _context.Vehicles
                .Include(v => v.Trips)
                .FirstOrDefaultAsync(v => v.VehicleId == id && v.DriverId == driverId);

            if (vehicle != null)
            {
                if (vehicle.Trips.Any())
                {
                    TempData["Error"] = "Không thể xóa xe này vì đã có lịch sử chuyến đi gắn với nó!";
                }
                else
                {
                    if (!string.IsNullOrEmpty(vehicle.VehicleImage))
                    {
                        var filepath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", vehicle.VehicleImage.TrimStart('/'));
                        if (System.IO.File.Exists(filepath))
                        {
                            System.IO.File.Delete(filepath);
                        }
                    }

                    _context.Vehicles.Remove(vehicle);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Đã xóa xe thành công!";
                }
            }

            return RedirectToAction("ManageVehicles");
        }

        // GET: Chấp nhận 1 đơn hàng tự do cho 1 chuyến xe của mình (Ghép chuyến)
        [HttpGet]
        public async Task<IActionResult> AcceptGenericOrder(int requestId, int tripId)
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            var request = await _context.Shiprequests
                .Include(r => r.Cargodetails)
                .FirstOrDefaultAsync(r => r.Id == requestId && (r.Status == 0 || r.Status == null) && r.TripId == null);
                
            var trip = await _context.Trips
                .Include(t => t.Vehicle)
                .Include(t => t.RouteTypeNavigation)
                .FirstOrDefaultAsync(t => t.TripId == tripId && t.DriverId == driverId);

            if (request != null && trip != null)
            {
                request.TripId = tripId;
                request.Status = 1; // Đã xác nhận
                request.PickupTimeTo = trip.ArrivalTime;

                var cargo = request.Cargodetails.FirstOrDefault();
                if (cargo != null)
                {
                    var vwFactorConfig = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.KeyName == "VolumeToWeightFactor");
                    int vwFactor = (int)(vwFactorConfig?.Value ?? 250);

                    var minPriceConfig = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.KeyName == "MinPrice");
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
                    decimal priceAfterCargo = basePrice * tripTypeMultiplier;
                    
                    request.TotalPrice = Math.Max(priceAfterCargo, minPrice);
                    trip.AvaiCapacityKg -= (int)weight;
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = $"Đã ghép đơn hàng #{requestId} vào chuyến xe của bạn thành công!";
            }
            else
            {
                TempData["Error"] = "Không thể nhận đơn hàng này. Có thể đơn đã được người khác nhận hoặc không hợp lệ.";
            }

            return RedirectToAction("Index");
        }
        public class IntermediateStationDto
    {
        public int stationId { get; set; }
        public double? distance { get; set; }
        public DateTime? estArrivalTime { get; set; }
    }
}
}