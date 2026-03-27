using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using TimChuyenDi.Models;
using System.Linq;
using System.Collections.Generic;

namespace TimChuyenDi.Controllers
{
    public class AuthController : Controller
    {
        private readonly TimchuyendiContext _context;
        private static Dictionary<string, (string Otp, DateTime Expiry)> _otpStore = new();
        // Lưu thông tin đăng ký tạm chờ xác thực OTP
        private static Dictionary<string, (string Name, string Phone, string Email, string Password, int Role)> _pendingRegister = new();

        public AuthController(TimchuyendiContext context)
        {
            _context = context;
        }

        // ==========================================
        // ĐĂNG NHẬP
        // ==========================================
        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string phone, string password, bool rememberMe)
        {
            var user = _context.Users.SingleOrDefault(u => u.Phone == phone);

            if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                if (!user.IsActive.GetValueOrDefault())
                {
                    var admin = _context.Users.FirstOrDefault(u => u.Role == 1);
                    ViewBag.Error = $"Tài khoản của bạn đã bị khóa!<br/>Liên hệ: <b>{admin?.Email ?? "admin@gmail.com"}</b>";
                    return View();
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim("UserId", user.UserId.ToString()),
                    new Claim(ClaimTypes.Role, user.Role.ToString())
                };

                await HttpContext.SignInAsync("Cookies",
                    new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")),
                    new AuthenticationProperties
                    {
                        IsPersistent = rememberMe,
                        AllowRefresh = true,
                        ExpiresUtc = rememberMe ? DateTime.UtcNow.AddDays(7) : null
                    });

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

        // ==========================================
        // ĐĂNG XUẤT
        // ==========================================
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("Cookies");
            return RedirectToAction("Login");
        }

        // ==========================================
        // ĐĂNG KÝ – BƯỚC 1: NHẬP THÔNG TIN → GỬI OTP
        // ==========================================
        [HttpGet]
        public IActionResult Register() => View();

        [HttpPost]
        public IActionResult Register(string name, string phone, string email, string password, int role = 2)
        {
            if (_context.Users.Any(u => u.Phone == phone))
            {
                ViewBag.Error = "Số điện thoại đã tồn tại!";
                return View();
            }

            if (string.IsNullOrEmpty(password) || password.Length < 6)
            {
                ViewBag.Error = "Mật khẩu phải có ít nhất 6 ký tự!";
                return View();
            }

            // Lưu thông tin tạm
            _pendingRegister[phone] = (name, phone, email, password, role);

            // Tạo OTP
            var otp = new Random().Next(100000, 999999).ToString();
            _otpStore[phone] = (otp, DateTime.Now.AddMinutes(5));

            TempData["RegPhone"] = phone;
            TempData["RegOtpCode"] = otp; // Hiển thị demo
            TempData["SuccessMessage"] = $"Mã OTP đã được gửi tới SĐT {phone}";

            return RedirectToAction("VerifyRegisterOtp");
        }

        // ==========================================
        // ĐĂNG KÝ – BƯỚC 2: XÁC THỰC OTP
        // ==========================================
        [HttpGet]
        public IActionResult VerifyRegisterOtp()
        {
            ViewBag.Phone = TempData["RegPhone"]?.ToString();
            ViewBag.OtpDemo = TempData["RegOtpCode"]?.ToString();
            if (string.IsNullOrEmpty(ViewBag.Phone))
                return RedirectToAction("Register");
            return View();
        }

        [HttpPost]
        public IActionResult VerifyRegisterOtp(string Phone, string OtpCode)
        {
            if (!_otpStore.ContainsKey(Phone))
            {
                ViewBag.Error = "Phiên xác thực đã hết hạn, vui lòng đăng ký lại!";
                return View();
            }

            var stored = _otpStore[Phone];
            if (DateTime.Now > stored.Expiry)
            {
                _otpStore.Remove(Phone);
                _pendingRegister.Remove(Phone);
                ViewBag.Error = "Mã OTP đã hết hạn. Vui lòng đăng ký lại!";
                return View();
            }

            if (stored.Otp != OtpCode)
            {
                ViewBag.Error = "Mã OTP không chính xác!";
                ViewBag.Phone = Phone;
                return View();
            }

            // OTP đúng → tạo tài khoản
            _otpStore.Remove(Phone);

            if (!_pendingRegister.ContainsKey(Phone))
            {
                ViewBag.Error = "Không tìm thấy thông tin đăng ký. Vui lòng thử lại!";
                return View();
            }

            var reg = _pendingRegister[Phone];
            _pendingRegister.Remove(Phone);

            // Kiểm tra lại trùng SĐT (phòng trường hợp race condition)
            if (_context.Users.Any(u => u.Phone == Phone))
            {
                TempData["SuccessMessage"] = "Số điện thoại đã tồn tại. Hãy đăng nhập!";
                return RedirectToAction("Login");
            }

            _context.Users.Add(new User
            {
                Name = reg.Name, Phone = reg.Phone, Email = reg.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(reg.Password),
                PasswordDemo = reg.Password, Role = reg.Role, IsActive = true, CreatedAt = DateTime.Now
            });
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Đăng ký thành công! Hãy đăng nhập.";
            return RedirectToAction("Login");
        }

        // ==========================================
        // THÔNG TIN CÁ NHÂN (XEM + SỬA)
        // ==========================================
        [Authorize]
        [HttpGet]
        public IActionResult Profile()
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var user = _context.Users.Find(userId);
            if (user == null) return RedirectToAction("Login");
            return View(user);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Profile(string Name, string Email)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var user = _context.Users.Find(userId);
            if (user == null) return RedirectToAction("Login");

            user.Name = Name;
            user.Email = Email;
            _context.SaveChanges();

            // Cập nhật lại cookie claims với tên mới
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Name),
                new Claim("UserId", user.UserId.ToString()),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            };
            await HttpContext.SignOutAsync("Cookies");
            await HttpContext.SignInAsync("Cookies",
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")),
                new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

            TempData["SuccessMessage"] = "Cập nhật thông tin thành công!";
            return RedirectToAction("Profile");
        }

        // ==========================================
        // ĐỔI MẬT KHẨU
        // ==========================================
        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword() => View();

        [Authorize]
        [HttpPost]
        public IActionResult ChangePassword(string CurrentPassword, string NewPassword, string ConfirmPassword)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var user = _context.Users.Find(userId);
            if (user == null) return RedirectToAction("Login");

            if (!BCrypt.Net.BCrypt.Verify(CurrentPassword, user.PasswordHash))
            {
                ViewBag.Error = "Mật khẩu hiện tại không đúng!";
                return View();
            }

            if (NewPassword != ConfirmPassword)
            {
                ViewBag.Error = "Mật khẩu mới và xác nhận không khớp!";
                return View();
            }

            if (NewPassword.Length < 6)
            {
                ViewBag.Error = "Mật khẩu mới phải có ít nhất 6 ký tự!";
                return View();
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            user.PasswordDemo = NewPassword;
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Đổi mật khẩu thành công!";
            return RedirectToAction("Profile");
        }

        // ==========================================
        // QUÊN MẬT KHẨU – BƯỚC 1: NHẬP SĐT
        // ==========================================
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        public IActionResult ForgotPassword(string Phone)
        {
            var user = _context.Users.FirstOrDefault(u => u.Phone == Phone);
            if (user == null)
            {
                ViewBag.Error = "Số điện thoại không tồn tại trong hệ thống!";
                return View();
            }

            var otp = new Random().Next(100000, 999999).ToString();
            _otpStore[Phone] = (otp, DateTime.Now.AddMinutes(5));

            TempData["OtpPhone"] = Phone;
            TempData["OtpCode"] = otp;
            TempData["SuccessMessage"] = $"Mã OTP đã được gửi tới SĐT {Phone}";

            return RedirectToAction("VerifyOtp");
        }

        // ==========================================
        // QUÊN MẬT KHẨU – BƯỚC 2: NHẬP MÃ OTP
        // ==========================================
        [HttpGet]
        public IActionResult VerifyOtp()
        {
            ViewBag.Phone = TempData["OtpPhone"]?.ToString();
            ViewBag.OtpDemo = TempData["OtpCode"]?.ToString();
            if (string.IsNullOrEmpty(ViewBag.Phone))
                return RedirectToAction("ForgotPassword");
            return View();
        }

        [HttpPost]
        public IActionResult VerifyOtp(string Phone, string OtpCode)
        {
            if (!_otpStore.ContainsKey(Phone))
            {
                ViewBag.Error = "Phiên xác thực đã hết hạn, vui lòng thử lại!";
                return View();
            }

            var stored = _otpStore[Phone];
            if (DateTime.Now > stored.Expiry)
            {
                _otpStore.Remove(Phone);
                ViewBag.Error = "Mã OTP đã hết hạn (5 phút). Vui lòng gửi lại!";
                ViewBag.Phone = Phone;
                return View();
            }

            if (stored.Otp != OtpCode)
            {
                ViewBag.Error = "Mã OTP không chính xác!";
                ViewBag.Phone = Phone;
                return View();
            }

            _otpStore.Remove(Phone);
            TempData["ResetPhone"] = Phone;
            return RedirectToAction("ResetPassword");
        }

        // ==========================================
        // QUÊN MẬT KHẨU – BƯỚC 3: ĐẶT MẬT KHẨU MỚI
        // ==========================================
        [HttpGet]
        public IActionResult ResetPassword()
        {
            ViewBag.Phone = TempData["ResetPhone"]?.ToString();
            if (string.IsNullOrEmpty(ViewBag.Phone))
                return RedirectToAction("ForgotPassword");
            return View();
        }

        [HttpPost]
        public IActionResult ResetPassword(string Phone, string NewPassword, string ConfirmPassword)
        {
            var user = _context.Users.FirstOrDefault(u => u.Phone == Phone);
            if (user == null)
            {
                ViewBag.Error = "Không tìm thấy tài khoản!";
                return View();
            }

            if (NewPassword != ConfirmPassword)
            {
                ViewBag.Error = "Mật khẩu mới và xác nhận không khớp!";
                ViewBag.Phone = Phone;
                return View();
            }

            if (NewPassword.Length < 6)
            {
                ViewBag.Error = "Mật khẩu phải có ít nhất 6 ký tự!";
                ViewBag.Phone = Phone;
                return View();
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            user.PasswordDemo = NewPassword;
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Đặt lại mật khẩu thành công! Hãy đăng nhập.";
            return RedirectToAction("Login");
        }

        // ==========================================
        // TOOL: Fix password null
        // ==========================================
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
    }
}