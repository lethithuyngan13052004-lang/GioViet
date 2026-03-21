using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using TimChuyenDi.Models;
using System.Linq;
using System.Collections.Generic;

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
            var user = _context.Users.SingleOrDefault(u => u.Phone == phone);

            if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                if (user.IsActive == false)
                {
                    ViewBag.Error = "Tài khoản của bạn đã bị khóa bởi Admin.";
                    return View();
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim("UserId", user.UserId.ToString()),
                    new Claim(ClaimTypes.Role, user.Role.ToString())
                };

                var identity = new ClaimsIdentity(claims, "Cookies");
                var principal = new ClaimsPrincipal(identity);

                HttpContext.SignInAsync("Cookies", principal);

                // Phân quyền theo DB: 1 là Admin, 2 là Customer, 3 là Driver
                if (user.Role == 1) return RedirectToAction("Index", "Admin");
                if (user.Role == 3) return RedirectToAction("Index", "Driver");
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

        // ========================================== 
        // 4. HÀM CHỮA CHÁY (Bản vá lỗi NULL PasswordDemo)
        // ==========================================
        [HttpGet]
        public IActionResult SetupPasswords()
        {
            var users = _context.Users.ToList();
            int count = 0;

            foreach (var u in users)
            {
                // Thêm điều kiện: Nếu PasswordDemo đang bị NULL thì cũng lôi ra cập nhật lại luôn!
                if (string.IsNullOrEmpty(u.PasswordHash) || !u.PasswordHash.StartsWith("$2a$") || string.IsNullOrEmpty(u.PasswordDemo))
                {
                    u.PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");
                    u.PasswordDemo = "123456"; // Fill luôn dữ liệu vào chỗ trống
                    count++;
                }
            }
            _context.SaveChanges();
            return Content($"BÁO CÁO: Đã quét Database và cập nhật thành công {count} tài khoản!");
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
            var exists = _context.Users.Any(u => u.Phone == phone);
            if (exists)
            {
                ViewBag.Error = "Số điện thoại này đã được đăng ký! Vui lòng dùng số khác.";
                return View();
            }

            var newUser = new User
            {
                Name = name,
                Phone = phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                PasswordDemo = password, // FIX: Lưu luôn pass dạng text để thầy cô dễ test
                Role = role,
                IsActive = true
            };

            _context.Users.Add(newUser);
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Đăng ký thành công! Vui lòng đăng nhập.";
            return RedirectToAction("Login");
        }
    }
}