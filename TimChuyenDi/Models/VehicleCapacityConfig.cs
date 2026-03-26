using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class VehicleCapacityConfig
{
    public int Id { get; set; }

    public int VehicleTypeId { get; set; }

    public int MinWeight { get; set; }

    public int MaxWeight { get; set; }

    public float EstimatedVolume { get; set; }
}
