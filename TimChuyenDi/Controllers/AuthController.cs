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

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [HttpPost]
        public async Task<IActionResult> Login(string phone, string password, bool rememberMe)
        {
            var user = _context.Users.SingleOrDefault(u => u.Phone == phone);

            if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                if (!user.IsActive.GetValueOrDefault()) // nếu null thì coi như false
                {
                    ViewBag.Error = "Tài khoản bị khóa!";
                    return View();
                }

                // Tạo claims
                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("UserId", user.UserId.ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString()) // hoặc chuyển sang string 1=Admin,2=Customer,3=Driver nếu muốn
        };

                var identity = new ClaimsIdentity(claims, "Cookies");
                var principal = new ClaimsPrincipal(identity);

                // Sign in với cookie
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = rememberMe,        // ✅ chỉ lưu lâu nếu tick
                    AllowRefresh = true,              // ✅ tự gia hạn khi user hoạt động
                    ExpiresUtc = rememberMe ? DateTime.UtcNow.AddDays(7) : (DateTime?)null
                };

                await HttpContext.SignInAsync("Cookies", principal, authProperties);

                // Redirect theo role
                return user.Role switch
                {
                    1 => RedirectToAction("Index", "Admin"),
                    3 => RedirectToAction("Index", "Driver"),
                    _ => RedirectToAction("Index", "Home")
                };
            }

            ViewBag.Error = "Sai tài khoản hoặc mật khẩu!";
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("Cookies");
            return RedirectToAction("Login", "Auth");
        }

        // Fix password null
        [HttpGet]
        public IActionResult SetupPasswords()
        {
            var users = _context.Users.ToList();
            int count = 0;

            foreach (var u in users)
            {
                if (string.IsNullOrEmpty(u.PasswordHash) || !u.PasswordHash.StartsWith("$2a$") || string.IsNullOrEmpty(u.PasswordDemo))
                {
                    u.PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");
                    u.PasswordDemo = "123456";
                    count++;
                }
            }

            _context.SaveChanges();
            return Content($"Đã cập nhật {count} tài khoản!");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Register(string name, string phone, string email, string password)
        {
            var exists = _context.Users.Any(u => u.Phone == phone);
            if (exists)
            {
                ViewBag.Error = "Số điện thoại đã tồn tại!";
                return View();
            }

            var newUser = new User
            {
                Name = name,
                Phone = phone,
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                PasswordDemo = password,
                Role = 2, // mặc định khách
                IsActive = true
            };

            _context.Users.Add(newUser);
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Đăng ký thành công!";
            return RedirectToAction("Login");
        }
    }
}