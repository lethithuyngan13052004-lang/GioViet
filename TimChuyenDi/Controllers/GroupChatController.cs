using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TimChuyenDi.Hubs;
using TimChuyenDi.Models;
using TimChuyenDi.Services;

namespace TimChuyenDi.Controllers
{
    [Authorize]
    public class GroupChatController : Controller
    {
        private readonly TimchuyendiContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly BehaviorService _behaviorService;

        public GroupChatController(TimchuyendiContext context, IHubContext<ChatHub> hubContext, BehaviorService behaviorService)
        {
            _context = context;
            _hubContext = hubContext;
            _behaviorService = behaviorService;
        }

        public async Task<IActionResult> Index(int id)
        {
            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return RedirectToAction("Login", "Auth");
            int currentUserId = int.Parse(userIdStr);
            var userRole = User.FindFirstValue(ClaimTypes.Role);

            var session = await _context.Chatsessions
                .Include(s => s.Req)
                .Include(s => s.Customer)
                .Include(s => s.Driver)
                .FirstOrDefaultAsync(s => s.SessionId == id);

            if (session == null) return NotFound("Phiên chat không tồn tại.");

            // Kiểm tra quyền truy cập (Khách, Tài xế của đơn, hoặc Admin)
            if (userRole != "1" && session.CustomerId != currentUserId && session.DriverId != currentUserId)
            {
                return Forbid("Bạn không có quyền truy cập cuộc trò chuyện này.");
            }

            ViewBag.CurrentUserId = currentUserId;
            ViewBag.UserRole = userRole;
            return View(session);
        }

        [HttpGet]
        public async Task<IActionResult> ChatByRequest(int requestId)
        {
            var session = await _context.Chatsessions.FirstOrDefaultAsync(s => s.ReqId == requestId);
            if (session == null)
            {
                TempData["Error"] = "Cuộc trò chuyện này chưa được khởi tạo hoặc không tồn tại.";
                return Redirect(Request.Headers["Referer"].ToString() ?? "/");
            }
            return RedirectToAction("Index", new { id = session.SessionId });
        }

        [HttpGet]
        public async Task<IActionResult> GetHistory(int id)
        {
            var messages = await _context.Chatmessages
                .Where(m => m.SessionId == id)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new {
                    m.SenderId,
                    m.SenderRole,
                    m.Message,
                    CreatedAt = m.CreatedAt.ToString("HH:mm dd/MM"),
                    SenderName = m.SenderRole == "bot" ? "Trợ lý Gió Việt" : m.Sender.Name
                })
                .ToListAsync();

            return Json(messages);
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(int sessionId, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return Json(new { success = false });

            var userIdStr = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            int currentUserId = int.Parse(userIdStr);
            var userRole = User.FindFirstValue(ClaimTypes.Role);

            var session = await _context.Chatsessions.FindAsync(sessionId);
            if (session == null) return Json(new { success = false });

            // Lưu tin nhắn vào DB
            var chatMsg = new Chatmessage
            {
                SessionId = sessionId,
                SenderId = currentUserId,
                Message = message,
                SenderRole = userRole == "3" ? "driver" : (userRole == "2" ? "customer" : "admin"),
                CreatedAt = DateTime.Now
            };

            _context.Chatmessages.Add(chatMsg);
            await _context.SaveChangesAsync();

            // Broadcast qua SignalR
            var senderName = User.FindFirstValue(ClaimTypes.Name) ?? "Người dùng";
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveMessage", 
                currentUserId.ToString(), message, chatMsg.SenderRole, chatMsg.CreatedAt.ToString("HH:mm dd/MM"), senderName);

            // Chatbot "Listen" và log hành vi (Background)
            _ = Task.Run(() => _behaviorService.ExtractAndLogBehaviorAsync(currentUserId, message));

            return Json(new { success = true });
        }
    }
}
