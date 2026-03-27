using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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

            // Định dạng Dropdown Trạm: "Tên Trạm (Tên Tỉnh)" cho dễ nhìn
            var stations = _context.Stations.Include(s => s.Province).ToList();
            var stationList = stations.Select(s => new {
                StationId = s.StationId,
                DisplayName = s.StationName + " (" + s.Province.ProvinceName + ")"
            }).ToList();

            ViewBag.Stations = new SelectList(stationList, "StationId", "DisplayName");

            // Chỉ hiển thị xe của ông tài xế đang đăng nhập và ĐÃ ĐƯỢC DUYỆT (Status = 1)
            var myVehicles = _context.Vehicles.Where(v => v.DriverId == driverId && v.Status == 1).ToList();
            ViewBag.Vehicles = new SelectList(myVehicles, "VehicleId", "PlateNumber");

            return View();
        }

        // POST: Xử lý lưu chuyến xe mới vào Database
        [HttpPost]
        public IActionResult CreateTrip(Trip model)
        {
            var driverId = int.Parse(User.FindFirstValue("UserId"));

            // Lấy thông tin Xe để "hút" sức chứa tối đa ném vào chuyến đi (phải được duyệt)
            var vehicle = _context.Vehicles.FirstOrDefault(v => v.VehicleId == model.VehicleId && v.DriverId == driverId && v.Status == 1);

            if (vehicle != null)
            {
                var newTrip = new Trip
                {
                    DriverId = driverId,
                    VehicleId = model.VehicleId,
                    RouteType = model.RouteType, // 1: Chạy thẳng cao tốc, 2: Rẽ trạm con
                    FromStation = model.FromStation,
                    ToStation = model.ToStation,
                    StartTime = model.StartTime,
                    ArrivalTime = model.ArrivalTime,
                    BasePrice = model.BasePrice,
                    Distance = model.Distance,
                    AvaiCapacityKg = vehicle.CapacityKg, // Bắt tự động từ bảng Vehicles
                    AvaiCapacityM3 = vehicle.CapacityM3  // Bắt tự động từ bảng Vehicles
                };

                _context.Trips.Add(newTrip);
                _context.SaveChanges();
                TempData["Success"] = "Lên lịch chuyến xe mới thành công!";
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
    }
}