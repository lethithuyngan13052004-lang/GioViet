using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class District
{
    public int DistrictId { get; set; }

    public int ProvinceId { get; set; }

    public string DistrictName { get; set; } = null!;

    public virtual Province Province { get; set; } = null!;

    public virtual ICollection<Station> Stations { get; set; } = new List<Station>();

    public virtual ICollection<Ward> Wards { get; set; } = new List<Ward>();
}
