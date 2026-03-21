using Microsoft.EntityFrameworkCore;
using TimChuyenDi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Đăng ký GeminiService vào hệ thống kèm theo HttpClient
builder.Services.AddHttpClient<TimChuyenDi.Services.GeminiService>();

// Đăng ký dịch vụ xác thực bằng Cookie
builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        // 🔐 Đường dẫn
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";

        // ⏳ Thời gian sống mặc định của cookie
        options.ExpireTimeSpan = TimeSpan.FromDays(7);

        // 🔄 Tự gia hạn khi user hoạt động
        options.SlidingExpiration = true;

        // 🔒 Bảo mật cơ bản
        options.Cookie.HttpOnly = true; // JS không đọc được cookie
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS thì mới secure
        options.Cookie.SameSite = SameSiteMode.Lax;

        // 🧠 Tên cookie (để dễ debug)
        options.Cookie.Name = "TimChuyenDiAuth";

        // (Optional) xử lý khi hết hạn
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.Redirect("/Auth/Login");
            return Task.CompletedTask;
        };
    });

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<TimchuyendiContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication(); // Kích hoạt kiểm tra đăng nhập
app.UseAuthorization();     // Kích hoạt kiểm tra quyền (Role)

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
