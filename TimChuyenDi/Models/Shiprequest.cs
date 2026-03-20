using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Shiprequest
{
    public int ReqId { get; set; }

    public int CustomerId { get; set; }

    public int TripId { get; set; }

    public int CargoTypeId { get; set; }

    public string? PickupAddress { get; set; }

    public decimal? PickupLat { get; set; }

    public decimal? PickupLng { get; set; }

    public string? DeliveryAddress { get; set; }

    public decimal? DeliveryLat { get; set; }

    public decimal? DeliveryLng { get; set; }

    public string ReceiverInfo { get; set; } = null!;

    public int Weight { get; set; }

    public string? Size { get; set; }

    public string? Description { get; set; }

    public decimal BasePrice { get; set; }

    public decimal PickupFee { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal TotalPrice { get; set; }

    /// <summary>
    /// 0: Pending, 1: Accepted, 2: Rejected, 3: Shipping, 4: Done
    /// </summary>
    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public virtual Cargotype CargoType { get; set; } = null!;

    public virtual Chatsession? Chatsession { get; set; }

    public virtual User Customer { get; set; } = null!;

    public virtual ICollection<Rating> Ratings { get; set; } = new List<Rating>();

    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();

    public virtual ICollection<RequestTripMatch> RequestTripMatches { get; set; } = new List<RequestTripMatch>();

    public virtual Trip Trip { get; set; } = null!;
}
