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
        public async Task<IActionResult> Login(string phone, string password)
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

            // 👉 Lưu role dạng int nhưng convert sang string tại đây
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

                var identity = new ClaimsIdentity(claims, "Cookies");
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync("Cookies", principal);

                // 👉 Redirect theo role int
                return user.Role switch
                {
                    1 => RedirectToAction("Index", "Admin"),
                    3 => RedirectToAction("Index", "Driver"),
                    _ => RedirectToAction("Index", "Home")
                };
            }

            ViewBag.Error = "Số điện thoại hoặc mật khẩu không đúng!";
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