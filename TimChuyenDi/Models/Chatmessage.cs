using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Chatmessage
{
    public int MessageId { get; set; }

    public int SessionId { get; set; }

    public int SenderId { get; set; }

    /// <summary>
    /// customer / driver / bot
    /// </summary>
    public string SenderRole { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? MessageType { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool? IsEdited { get; set; }

    public DateTime? EditedAt { get; set; }

    public bool? IsDeleted { get; set; }

    public bool? IsImportant { get; set; }

    public virtual User Sender { get; set; } = null!;

    public virtual Chatsession Session { get; set; } = null!;
}
