using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Cargodetail
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    public decimal? Weight { get; set; }

    public decimal? Length { get; set; }

    public decimal? Width { get; set; }

    public decimal? Height { get; set; }

    public string? Description { get; set; }

    public virtual Shiprequest Request { get; set; } = null!;
}
