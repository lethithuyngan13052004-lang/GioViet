using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Ward
{
    public int WardId { get; set; }

    public int DistrictId { get; set; }

    public string WardName { get; set; } = null!;

    public virtual District District { get; set; } = null!;

    public virtual ICollection<Station> Stations { get; set; } = new List<Station>();
}
