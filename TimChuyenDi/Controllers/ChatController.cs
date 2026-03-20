using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimChuyenDi.Services;
using TimChuyenDi.Models; // Khai báo để dùng ApplicationDbContext
using System.Threading.Tasks;
using System.Linq;
using System;

namespace TimChuyenDi.Controllers
{
    public class ChatController : Controller
    {
        private readonly GeminiService _geminiService;
        private readonly TimchuyendiContext _context;

        // "Tiêm" cả GeminiService và ApplicationDbContext vào Controller
        public ChatController(GeminiService geminiService, TimchuyendiContext context)
        {
            _geminiService = geminiService;
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        // Thêm tham số 'history' để nhận lịch sử từ giao diện
        public async Task<IActionResult> SendMessage(string userMessage, string history)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return Json(new { success = false, reply = "Vui lòng nhập tin nhắn." });
            }

            // Lấy nhiều chuyến xe hơn (tối đa 30 chuyến) để AI có dữ liệu so sánh giá
            var trips = _context.Trips
                .Include(t => t.FromLocationNavigation)
                .Include(t => t.ToLocationNavigation)
                .Include(t => t.Driver)
                .Where(t => t.StartTime > DateTime.Now)
                .OrderBy(t => t.StartTime)
                .Take(30)
                .ToList();

            // 1. Nhấn mạnh việc lấy TripId (Mã chuyến xe) để làm link
            string dbContextInfo = "DANH SÁCH CHUYẾN XE ĐANG HOẠT ĐỘNG:\n";
            if (!trips.Any())
            {
                dbContextInfo += "- Hiện tại không có chuyến xe nào.\n";
            }
            else
            {
                foreach (var t in trips)
                {
                    // Thêm chữ "Mã chuyến xe" rõ ràng để AI lấy dữ liệu ghép vào Link
                    dbContextInfo += $"- Mã chuyến xe: {t.TripId} | Tuyến: {t.FromLocationNavigation.ProvinceName} đi {t.ToLocationNavigation.ProvinceName} | Giá cước: {t.BasePricePerKg}đ/kg | Khởi hành: {t.StartTime:dd/MM/yyyy HH:mm} | Tài xế: {t.Driver.Name} | Chỗ trống: {t.AvaiCapacityKg}kg.\n";
                }
            }

            string secretPrompt = $@"
Bạn là chuyên gia điều phối vận tải xuất sắc của nền tảng 'Ghép Hàng Liên Tỉnh'.
TUYỆT ĐỐI CHỈ DÙNG DỮ LIỆU CHUYẾN XE BÊN DƯỚI. KHÔNG bịa đặt, KHÔNG giới thiệu hãng khác.

{dbContextInfo}

LỊCH SỬ TRÒ CHUYỆN (để bạn hiểu ngữ cảnh nếu khách nói trống không):
{history}

KỸ NĂNG BẮT BUỘC (QUAN TRỌNG):
1. Tính toán giá cả rẻ nhất dựa trên số Kg khách gửi (nếu có).
2. MỖI KHI BẠN GỢI Ý MỘT CHUYẾN XE CỤ THỂ, bạn BẮT BUỘC phải đính kèm nút Đặt Xe bằng cách chèn chính xác đoạn mã HTML sau (thay số [ID] bằng Mã chuyến xe tương ứng trong danh sách):
<br/><a href='/Customer/BookTrip/[ID]' target='_blank' class='btn btn-sm btn-success fw-bold mt-2 mb-2'>🚐 Đặt chuyến [ID] ngay</a><br/>
3. Luôn xưng 'tôi' và gọi khách là 'bạn'. Trình bày rõ ràng, xuống dòng dễ nhìn.

Câu hỏi hiện tại của khách: ""{userMessage}""";

            string aiReply = await _geminiService.SendMessageAsync(secretPrompt);

            return Json(new { success = true, reply = aiReply });
        }
    }
}