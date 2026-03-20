using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Rating
{
    public int RatingId { get; set; }

    public int CustomerId { get; set; }

    public int ReqId { get; set; }

    public int Score { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User Customer { get; set; } = null!;

    public virtual Shiprequest Req { get; set; } = null!;
}
