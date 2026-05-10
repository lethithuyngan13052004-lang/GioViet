using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models
{
    public enum TripStep
    {
        None = 0,
        AskVehicleAndType = 1,
        AskRoute = 2,
        AskTimeAndPrice = 3,
        Confirm = 4
    }

    public class ChatTripSession
    {
        public bool IsActive { get; set; } = false;
        public TripStep CurrentStep { get; set; } = TripStep.None;

        public int? LinkedReqId { get; set; } // Hỗ trợ lưu ID của Shiprequest muốn ghép

        // Intent Info
        public bool IsManagingOrders { get; set; } = false;
        
        // 1. Vehicle and Trip Type
        public int? VehicleId { get; set; }
        public int RouteType { get; set; } = 2; // Default 2: Ghép chuyến
        public int? DriverId { get; set; }

        // 2. Route
        public int? FromStationId { get; set; }
        public string? FromStationQuery { get; set; } // Temporary string the user said
        
        public int? ToStationId { get; set; }
        public string? ToStationQuery { get; set; }

        public List<int> IntermediateStationIds { get; set; } = new();
        public List<string> IntermediateQueries { get; set; } = new();

        public bool RouteIsDone { get; set; } = false;
        public bool IsRouteMainConfirmed { get; set; } = false;

        // 3. Time, Capacity and Price
        public DateTime? StartTime { get; set; }
        public int? AvaiCapacityKg { get; set; }
        public decimal? BasePrice { get; set; }
        
        // Caching calculation
        public double? EstDistance { get; set; }
        public double? EstDurationSec { get; set; }
        public DateTime? EstArrivalTime { get; set; }

        public bool IsConfirmed { get; set; } = false;
    }
}
