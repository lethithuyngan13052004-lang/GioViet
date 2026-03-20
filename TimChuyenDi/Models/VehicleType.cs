using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class VehicleType
{
    public int VehicleTypeId { get; set; }

    public string TypeName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();

    public virtual ICollection<Cargotype> CargoTypes { get; set; } = new List<Cargotype>();
}
