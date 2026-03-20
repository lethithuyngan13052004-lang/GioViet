using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class RequestTripMatch
{
    public int MatchId { get; set; }

    public int RequestId { get; set; }

    public int TripId { get; set; }

    /// <summary>
    /// Pending / Accepted / Rejected / Cancelled
    /// </summary>
    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? Note { get; set; }

    public virtual Shiprequest Request { get; set; } = null!;

    public virtual Trip Trip { get; set; } = null!;
}
