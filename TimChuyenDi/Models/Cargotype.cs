using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Cargotype
{
    public int CargoTypeId { get; set; }

    public string TypeName { get; set; } = null!;

    public decimal PriceMultiplier { get; set; }

    public virtual ICollection<Shiprequest> Shiprequests { get; set; } = new List<Shiprequest>();

    public virtual ICollection<VehicleType> VehicleTypes { get; set; } = new List<VehicleType>();
}
