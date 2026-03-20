using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Report
{
    public int ReportId { get; set; }

    public int ReporterId { get; set; }

    public int DriverId { get; set; }

    public int? ReqId { get; set; }

    public string Content { get; set; } = null!;

    /// <summary>
    /// 0: Pending, 1: Reviewed, 2: Resolved, 3: Rejected
    /// </summary>
    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public virtual User Driver { get; set; } = null!;

    public virtual User Reporter { get; set; } = null!;

    public virtual Shiprequest? Req { get; set; }
}
