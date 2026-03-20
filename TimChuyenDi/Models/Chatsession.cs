using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Chatsession
{
    public int SessionId { get; set; }

    public int ReqId { get; set; }

    public int DriverId { get; set; }

    public int CustomerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// 0: Active, 1: Closed, 2: Cancelled
    /// </summary>
    public int Status { get; set; }

    public virtual ICollection<Chatmessage> Chatmessages { get; set; } = new List<Chatmessage>();

    public virtual User Customer { get; set; } = null!;

    public virtual User Driver { get; set; } = null!;

    public virtual Shiprequest Req { get; set; } = null!;
}
