using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class TripStation
{
    public int TripStationId { get; set; }

    public int TripId { get; set; }

    public int StationId { get; set; }

    public int StopOrder { get; set; }

    public DateTime? EstArrivalTime { get; set; }
    public DateTime? ArrivalTime { get; set; }
    public double? DistanceFromPrev { get; set; }
    public virtual Station Station { get; set; } = null!;

    public virtual Trip Trip { get; set; } = null!;
}
