using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Vehicle
{
    public int VehicleId { get; set; }

    public int DriverId { get; set; }

    public string PlateNumber { get; set; } = null!;

    public int CapacityKg { get; set; }

    public int CapacityM3 { get; set; }

    public int VehicleTypeId { get; set; }

    /// <summary>
    /// 0: Pending, 1: Approved, 2: Rejected
    /// </summary>
    public int Status { get; set; }
    
    public string? VehicleImage { get; set; }

    public virtual User Driver { get; set; } = null!;

    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();

    public virtual VehicleType VehicleType { get; set; } = null!;
}
