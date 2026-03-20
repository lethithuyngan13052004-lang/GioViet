using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using TimChuyenDi.Models; 
using System.Linq;

namespace TimChuyenDi.Controllers
{
    public class AuthController : Controller
    {
        private readonly TimchuyendiContext _context;

        public AuthController(TimchuyendiContext context)
        {
            _context = context;
        }

        // 1. Giao diện trang đăng nhập
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // 2. Xử lý khi người dùng ấn nút "Đăng nhập"
        [HttpPost]
        public IActionResult Login(string phone, string password)
        {
            // Tìm user theo số điện thoại (Phone là tên đăng nhập)
            var user = _context.Users.SingleOrDefault(u => u.Phone == phone);

            // Kiểm tra user có tồn tại và mật khẩu đã băm (BCrypt) có khớp không
            if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                // Kiểm tra tài khoản có bị khóa không (1: Hoạt động, 0: Khóa)
                if (user.IsActive == false)
                {
                    ViewBag.Error = "Tài khoản của bạn đã bị khóa bởi Admin.";
                    return View();
                }

                // Khởi tạo các thông tin lưu vào phiên đăng nhập (Cookie)
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim("UserId", user.UserId.ToString()),
                    new Claim(ClaimTypes.Role, user.Role.ToString())
                };

                var identity = new ClaimsIdentity(claims, "Cookies");
                var principal = new ClaimsPrincipal(identity);

                // Thực hiện ghi Cookie
                HttpContext.SignInAsync("Cookies", principal);

                // Phân luồng chuyển hướng dựa trên Role (0: Admin, 1: Driver, 2: Customer)
                if (user.Role == 0) return RedirectToAction("Index", "Admin");
                if (user.Role == 1) return RedirectToAction("Index", "Driver");

                // Khách hàng (Customer) đăng nhập thành công sẽ về trang chủ tìm xe
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Số điện thoại hoặc mật khẩu không đúng!";
            return View();
        }

        // 3. Xử lý Đăng xuất
        public IActionResult Logout()
        {
            HttpContext.SignOutAsync("Cookies");
            return RedirectToAction("Login", "Auth");
        }

        // 4. MẸO NHỎ ĐỂ TEST: Hàm này giúp bạn reset mật khẩu của dữ liệu mẫu thành "123456" chuẩn BCrypt
        [HttpGet]
        public IActionResult SetupPasswords()
        {
            var users = _context.Users.ToList();
            foreach (var u in users)
            {
                u.PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");
            }
            _context.SaveChanges();
            return Content("Đã cập nhật mật khẩu tất cả user mẫu thành: 123456");
        }

        // GET: Hiển thị form đăng ký
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // POST: Xử lý lưu tài khoản mới
        [HttpPost]
        public IActionResult Register(string name, string phone, string password, int role)
        {
            // Kiểm tra xem số điện thoại đã tồn tại chưa
            var exists = _context.Users.Any(u => u.Phone == phone);
            if (exists)
            {
                ViewBag.Error = "Số điện thoại này đã được đăng ký! Vui lòng dùng số khác.";
                return View();
            }

            // Tạo người dùng mới
            var newUser = new User
            {
                Name = name,
                Phone = phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password), // Băm mật khẩu
                Role = role,
                IsActive = true,
               // CreatedAt = DateTime.Now
            };

            _context.Users.Add(newUser);
            _context.SaveChanges();

            // Đăng ký xong chuyển hướng về trang Đăng nhập
            TempData["SuccessMessage"] = "Đăng ký thành công! Vui lòng đăng nhập.";
            return RedirectToAction("Login");
        }
    }
}