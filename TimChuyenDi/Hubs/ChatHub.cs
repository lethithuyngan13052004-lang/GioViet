using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace TimChuyenDi.Hubs
{
    public class ChatHub : Hub
    {
        private readonly Models.TimchuyendiContext _context;

        public ChatHub(Models.TimchuyendiContext context)
        {
            _context = context;
        }

        public async Task JoinChat(string sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        }

        public async Task JoinAllMyGroups(string userId)
        {
            if (int.TryParse(userId, out int uId))
            {
                var sessionIds = _context.Chatsessions
                    .Where(s => s.CustomerId == uId || s.DriverId == uId)
                    .Select(s => s.SessionId.ToString())
                    .ToList();

                foreach (var sId in sessionIds)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, sId);
                }
            }
        }

        public async Task LeaveChat(string sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        }

        // We can use this to send messages in real-time, but we'll likely save to DB first 
        // and then broadcast via the controller for better persistence handling.
        public async Task SendMessage(string sessionId, string senderId, string message, string role)
        {
            await Clients.Group(sessionId).SendAsync("ReceiveMessage", senderId, message, role);
        }
    }
}
