using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Trip
{
    public int TripId { get; set; }

    public int DriverId { get; set; }

    public int VehicleId { get; set; }

    /// <summary>
    /// 1: Direct, 2: Multi-stop
    /// </summary>
    public int RouteType { get; set; }

    public int FromStation { get; set; }

    public int ToStation { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime? ArrivalTime { get; set; }

    public int AvaiCapacityKg { get; set; }

    public decimal BasePrice { get; set; }

    public decimal? Distance { get; set; }

    public decimal? TotalPrice { get; set; }

    public decimal? PlatformFee { get; set; }

    public decimal? DriverEarning { get; set; }

    // ====== NAVIGATION ======

    public virtual User Driver { get; set; } = null!;

    public virtual Station FromStationNavigation { get; set; } = null!;

    public virtual ICollection<RequestTripMatch> RequestTripMatches { get; set; } = new List<RequestTripMatch>();

    public virtual ICollection<Shiprequest> Shiprequests { get; set; } = new List<Shiprequest>();

    public virtual Station ToStationNavigation { get; set; } = null!;

    public virtual ICollection<TripStation> TripStations { get; set; } = new List<TripStation>();

    public virtual Vehicle Vehicle { get; set; } = null!;

    public virtual TripType RouteTypeNavigation { get; set; } = null!;

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}