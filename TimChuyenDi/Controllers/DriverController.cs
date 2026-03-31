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
        public IActionResult Index()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            // Nối bảng sâu: Đơn hàng -> Chuyến xe -> Trạm -> Tỉnh
            var requests = _context.Shiprequests
                .Include(r => r.User)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.FromStationNavigation)
                        .ThenInclude(s => s.Province)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.ToStationNavigation)
                        .ThenInclude(s => s.Province)
                .Where(r => r.Trip.DriverId == driverId)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return View(requests);
        }

        // POST: Xử lý cập nhật trạng thái đơn hàng nâng cao
        [HttpPost]
        public IActionResult UpdateRequestStatus(int reqId, int status)
        {
            var request = _context.Shiprequests
                                  .Include(r => r.Trip)
                                  .Include(r => r.Cargodetails)
                                  .FirstOrDefault(r => r.Id == reqId);

            if (request != null)
            {
                // 1. Chờ xác nhận (0) -> Nhận đơn (1) hoặc Từ chối (2)
                if (request.Status == 0 && (status == 1 || status == 2))
                {
                    request.Status = status;

                    if (status == 1) // Nếu nhận đơn -> Trừ sức chứa của xe
                    {
                        request.Trip.AvaiCapacityKg -= (int)(request.Cargodetails.FirstOrDefault()?.Weight ?? 0);
                        // Nếu sau này khách có nhập thể tích (Size), bro có thể trừ tiếp AvaiCapacityM3 ở đây
                    }
                    _context.SaveChanges();
                }
                // 2. Đã xác nhận (1) -> Đang giao (3) HOẶC Đang giao (3) -> Hoàn tất (4)
                else if ((request.Status == 1 && status == 3) || (request.Status == 3 && status == 4))
                {
                    request.Status = status;
                    _context.SaveChanges();
                }
            }
            return RedirectToAction("Index");
        }

        // GET: Xem danh sách các chuyến xe tài xế đã đăng
        public IActionResult MyTrips()
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));
            var trips = _context.Trips
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.Vehicle)
                .Include(t => t.RouteTypeNavigation)
                .Where(t => t.DriverId == driverId)
                .OrderByDescending(t => t.StartTime)
                .ToList();

            return View(trips);
        }

        // GET: Hiển thị form đăng chuyến xe mới
        [HttpGet]
        public IActionResult CreateTrip()
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));

            // Danh sách trạm đầy đủ để tra cứu trên Map
            var stations = _context.Stations
                .Include(s => s.Province)
                .OrderBy(s => s.StationName)
                .ToList();
            
            ViewBag.StationsRaw = stations.Select(s => new {
                s.StationId,
                s.StationName,
                s.Latitude,
                s.Longitude,
                ProvinceName = s.Province.ProvinceName,
                s.Address
            }).ToList();

            // Loại chuyến đi (Radio Buttons)
            ViewBag.TripTypes = _context.TripTypes.ToList();

            // Danh sách Tỉnh/Thành
            ViewBag.Provinces = _context.Provinces.OrderBy(p => p.ProvinceName).ToList();

            // Xe của tài xế đã duyệt
            var myVehicles = _context.Vehicles
                .Where(v => v.DriverId == driverId && v.Status == 1)
                .Include(v => v.VehicleType)
                .ToList();
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
            var driverId = int.Parse(User.FindFirstValue("UserId"));
            var vehicle = _context.Vehicles.FirstOrDefault(v => v.VehicleId == model.VehicleId && v.DriverId == driverId && v.Status == 1);

            if (vehicle != null)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    model.DriverId = driverId;
                    // Mặc định Capacity nếu tài xế không sửa
                    if (model.AvaiCapacityKg <= 0) model.AvaiCapacityKg = vehicle.CapacityKg;
                    if (model.AvaiCapacityM3 <= 0) model.AvaiCapacityM3 = vehicle.CapacityM3;

                    _context.Trips.Add(model);
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
                                await _context.SaveChangesAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ghi log lỗi để debug nếu cần
                            System.Diagnostics.Debug.WriteLine("JSON Parse Error: " + ex.Message);
                            // Có thể log vào Database hoặc file log ở đây
                        }
                    }

                    await transaction.CommitAsync();
                    TempData["Success"] = "Đăng chuyến xe và lộ trình thành công!";
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "Lỗi khi lưu chuyến xe: " + ex.Message;
                    return RedirectToAction("CreateTrip");
                }
            }

            return RedirectToAction("MyTrips");
        }

        // GET: Xem các đánh giá của khách hàng về tài xế
        [HttpGet]
        public IActionResult MyReviews()
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));

            // Logic mới: Đánh giá nối vào Đơn hàng (Req), Đơn hàng nối vào Chuyến xe (Trip)
            var reviews = _context.Ratings
                .Include(r => r.Customer)
                .Include(r => r.Req)
                    .ThenInclude(req => req.Trip)
                        .ThenInclude(t => t.FromStationNavigation)
                .Include(r => r.Req)
                    .ThenInclude(req => req.Trip)
                        .ThenInclude(t => t.ToStationNavigation)
                .Where(r => r.Req.Trip.DriverId == driverId)
                .OrderByDescending(r => r.RatingId)
                .ToList();

            ViewBag.AverageScore = reviews.Any() ? Math.Round(reviews.Average(r => r.Score), 1) : 0;
            ViewBag.TotalReviews = reviews.Count;

            return View(reviews);
        }

        // GET: Hiển thị form Sửa chuyến xe
        [HttpGet]
        public IActionResult EditTrip(int id)
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));
            var trip = _context.Trips.FirstOrDefault(t => t.TripId == id && t.DriverId == driverId);

            if (trip == null) return NotFound();

            var stations = _context.Stations.Include(s => s.Province).ToList();
            var stationList = stations.Select(s => new {
                StationId = s.StationId,
                DisplayName = s.StationName + " (" + s.Province.ProvinceName + ")"
            }).ToList();

            ViewBag.Stations = new SelectList(stationList, "StationId", "DisplayName");
            ViewBag.Vehicles = new SelectList(_context.Vehicles.Where(v => v.DriverId == driverId && v.Status == 1).ToList(), "VehicleId", "PlateNumber");

            return View(trip);
        }

        // POST: Xử lý lưu thông tin Sửa chuyến xe
        [HttpPost]
        public IActionResult EditTrip(Trip updatedTrip)
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));
            var trip = _context.Trips.FirstOrDefault(t => t.TripId == updatedTrip.TripId && t.DriverId == driverId);

            if (trip != null)
            {
                trip.VehicleId = updatedTrip.VehicleId;
                trip.RouteType = updatedTrip.RouteType;
                trip.FromStation = updatedTrip.FromStation;
                trip.ToStation = updatedTrip.ToStation;
                trip.StartTime = updatedTrip.StartTime;
                trip.ArrivalTime = updatedTrip.ArrivalTime;
                trip.AvaiCapacityKg = updatedTrip.AvaiCapacityKg;
                trip.BasePrice = updatedTrip.BasePrice;
                trip.Distance = updatedTrip.Distance;

                _context.SaveChanges();
                TempData["Success"] = "Cập nhật chuyến xe thành công!";
            }
            return RedirectToAction("MyTrips");
        }

        // POST: Xử lý Xóa chuyến xe
        [HttpPost]
        public IActionResult DeleteTrip(int id)
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));

            var trip = _context.Trips
                               .Include(t => t.Shiprequests)
                               .Include(t => t.Users) // 🔥 thêm dòng này
                               .FirstOrDefault(t => t.TripId == id && t.DriverId == driverId);

            if (trip != null)
            {
                if (trip.Shiprequests.Any())
                {
                    TempData["Error"] = "Không thể xóa vì đã có khách hàng đặt chỗ. Vui lòng từ chối đơn trước!";
                    return RedirectToAction("MyTrips");
                }

                // 🔥 Xóa quan hệ saved routes (many-to-many)
                trip.Users.Clear();

                _context.Trips.Remove(trip);
                _context.SaveChanges();

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
                .Include(r => r.Trip)
                    .ThenInclude(t => t.FromStationNavigation)
                        .ThenInclude(s => s.Province)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.ToStationNavigation)
                        .ThenInclude(s => s.Province)
                .Include(r => r.Trip)
                    .ThenInclude(t => t.Vehicle)
                .FirstOrDefault(r => r.Id == id && r.Trip.DriverId == driverId);

            if (requestDetail == null) return NotFound("Không tìm thấy đơn hàng!");

            return View(requestDetail);
        }

        // ==================================================
        // QUẢN LÝ XE CỦA TÀI XẾ
        // ==================================================

        // GET: Danh sách xe của tài xế
        [HttpGet]
        public IActionResult ManageVehicles()
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));
            var vehicles = _context.Vehicles
                .Include(v => v.VehicleType)
                .Where(v => v.DriverId == driverId)
                .OrderByDescending(v => v.VehicleId)
                .ToList();

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
                                           
                if (capacityConfig != null)
                {
                    model.CapacityM3 = (int)Math.Round(capacityConfig.EstimatedVolume);
                }
                else
                {
                    model.CapacityM3 = 0;
                }

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
        public IActionResult EditVehicle(int id)
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));
            var vehicle = _context.Vehicles.FirstOrDefault(v => v.VehicleId == id && v.DriverId == driverId);

            if (vehicle == null) return NotFound();

            ViewBag.VehicleTypes = new SelectList(_context.VehicleTypes.ToList(), "VehicleTypeId", "TypeName");
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

                var driverId = int.Parse(User.FindFirstValue("UserId"));
                
                // Kiểm tra trùng lặp biển số với xe khác
                if (_context.Vehicles.Any(v => v.PlateNumber == PlateNumber && v.VehicleId != VehicleId))
                {
                    TempData["Error"] = $"Biển số xe {PlateNumber} đã tồn tại ở một xe khác. Vui lòng kiểm tra lại!";
                    return RedirectToAction("ManageVehicles");
                }

                var vehicle = _context.Vehicles.FirstOrDefault(v => v.VehicleId == VehicleId && v.DriverId == driverId);

                if (vehicle != null)
                {
                    vehicle.PlateNumber = PlateNumber;
                    vehicle.CapacityKg = CapacityKg;

                    var capacityConfig = await _context.VehicleCapacityConfigs
                        .FirstOrDefaultAsync(c => c.VehicleTypeId == VehicleTypeId 
                                               && CapacityKg >= c.MinWeight 
                                               && CapacityKg <= c.MaxWeight);
                                               
                    if (capacityConfig != null)
                    {
                        vehicle.CapacityM3 = (int)Math.Round(capacityConfig.EstimatedVolume);
                    }
                    else
                    {
                        vehicle.CapacityM3 = 0;
                    }

                    vehicle.VehicleTypeId = VehicleTypeId;
                    vehicle.Status = 0; // Sửa xong lại chờ duyệt

                    if (imageFile != null && imageFile.Length > 0)
                    {
                        string uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "vehicles");
                        if (!Directory.Exists(uploadFolder))
                        {
                            Directory.CreateDirectory(uploadFolder);
                        }

                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(imageFile.FileName);
                        string filePath = Path.Combine(uploadFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(fileStream);
                        }

                        // Xóa file cũ (tuỳ chọn)
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
                TempData["Error"] = $"Hệ thống gặp lỗi: {ex.Message}{innerMsg}";
            }

            return RedirectToAction("ManageVehicles");
        }

        // GET: Hiển thị các đơn hàng chờ ghép (chưa có chuyến) có lộ trình phù hợp với tài xế
        [HttpGet]
        public IActionResult AvailableOrders()
        {
            var userIdStr = User.FindFirstValue("UserId");
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int driverId = int.Parse(userIdStr);

            // BƯỚC 1: Lấy danh sách các chuyến xe sắp chạy của tài xế này
            var activeTrips = _context.Trips
                .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
                .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
                .Where(t => t.DriverId == driverId && t.StartTime > DateTime.Now)
                .ToList();


            if (!activeTrips.Any())
            {
                ViewBag.Message = "Bạn chưa có chuyến xe nào sắp khởi hành. Vui lòng đăng chuyến xe trước để tìm đơn ghép!";
                return View(new List<Shiprequest>());
            }

            // Lấy danh sách các cặp Tỉnh đi - Tỉnh đến mà tài xế đang chạy
            var activeRoutes = activeTrips.Select(t => new { From = t.FromStationNavigation.ProvinceId, To = t.ToStationNavigation.ProvinceId }).Distinct().ToList();

            // BƯỚC 2: Tìm các đơn hàng chưa có chuyến (TripId == null) và khớp lộ trình
            var availableRequests = _context.Shiprequests
                .Include(r => r.User)
                .Include(r => r.Cargodetails)
                .Include(r => r.Shippingroutes)
                .Where(r => r.TripId == null && (r.Status == 0 || r.Status == null))
                .ToList() // Thực hiện lọc nốt phía Client nếu logic route phức tạp
                .Where(r => {
                    var route = r.Shippingroutes.FirstOrDefault();
                    return route != null && activeRoutes.Any(ar => ar.From == route.FromProvinceId && ar.To == route.ToProvinceId);
                })
                .OrderBy(r => r.ExpectedDeliveryDate ?? DateTime.MaxValue) // Ưu tiên ngày giao hàng mong muốn
                .ToList();

            ViewBag.ActiveTrips = activeTrips;
            return View(availableRequests);
        }

        // POST: Xóa xe
        [HttpPost]
        public IActionResult DeleteVehicle(int id)
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));
            var vehicle = _context.Vehicles
                .Include(v => v.Trips)
                .FirstOrDefault(v => v.VehicleId == id && v.DriverId == driverId);

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
                    _context.SaveChanges();
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

            var request = _context.Shiprequests
                .Include(r => r.Cargodetails)
                .FirstOrDefault(r => r.Id == requestId && (r.Status == 0 || r.Status == null) && r.TripId == null);
            
            var trip = _context.Trips.FirstOrDefault(t => t.TripId == tripId && t.DriverId == driverId);

            if (request != null && trip != null)
            {
                request.TripId = tripId;
                request.Status = 1; // Đã xác nhận
                request.ExpectedDeliveryDate = trip.ArrivalTime;

                
                var cargo = request.Cargodetails.FirstOrDefault();
                if (cargo != null)
                {
                    request.TotalPrice = trip.BasePrice;
                    trip.AvaiCapacityKg -= (int)(cargo.Weight ?? 0);
                }

                await _context.SaveChangesAsync();

                // Đồng bộ OrderCode
                request.OrderCode = "TC" + request.Id;
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