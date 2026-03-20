using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Behaviorlog
{
    public int BehaviorId { get; set; }

    public int UserId { get; set; }

    public string Action { get; set; } = null!;

    public string Object { get; set; } = null!;

    public string Value { get; set; } = null!;

    public float? Confidence { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
