using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Station
{
    public int StationId { get; set; }

    public string StationName { get; set; } = null!;

    public string Address { get; set; } = null!;

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public int ProvinceId { get; set; }

    public int? DistrictId { get; set; }

    public int? WardId { get; set; }

    public virtual District? District { get; set; }

    public virtual Province Province { get; set; } = null!;

    public virtual ICollection<Trip> TripFromStationNavigations { get; set; } = new List<Trip>();

    public virtual ICollection<TripStation> TripStations { get; set; } = new List<TripStation>();

    public virtual ICollection<Trip> TripToStationNavigations { get; set; } = new List<Trip>();

    public virtual Ward? Ward { get; set; }
    public virtual ICollection<Shippingroute> ShippingroutesFrom { get; set; } = new List<Shippingroute>();
    public virtual ICollection<Shippingroute> ShippingroutesTo { get; set; } = new List<Shippingroute>();
}

