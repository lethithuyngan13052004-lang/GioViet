using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using TimChuyenDi.Models;

namespace TimChuyenDi.Controllers
{
    [Authorize(Roles = "0")] // Số 0 là Role của Admin mà chúng ta đã định nghĩa
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
            var users = query.OrderByDescending(u => u.CreatedAt).ToList();

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
                .Include(t => t.FromLocationNavigation)
                .Include(t => t.ToLocationNavigation)
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
                        MaxCapacityKg = maxCapacityKg
                    };
                    _context.Vehicles.Add(newVehicle);
                    _context.SaveChanges();
                }
            }
            return RedirectToAction("ManageVehicles");
        }
    }
}