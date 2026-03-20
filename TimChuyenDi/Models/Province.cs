using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Province
{
    public int ProvinceId { get; set; }

    public string ProvinceName { get; set; } = null!;

    public virtual ICollection<District> Districts { get; set; } = new List<District>();

    public virtual ICollection<Station> Stations { get; set; } = new List<Station>();
}
