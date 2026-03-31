using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Shiprequest
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public string? OrderCode { get; set; }


    public int? TripId { get; set; }

    public decimal? TotalPrice { get; set; }

    /// <summary>
    /// 0: Pending, 1: Accepted, 2: Rejected, 3: Shipping, 4: Done
    /// </summary>
    public int? Status { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ExpectedDeliveryDate { get; set; }
    public virtual Chatsession? Chatsession { get; set; }


    public virtual User User { get; set; } = null!;

    public virtual ICollection<Rating> Ratings { get; set; } = new List<Rating>();

    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();

    public virtual ICollection<RequestTripMatch> RequestTripMatches { get; set; } = new List<RequestTripMatch>();

    public virtual Trip? Trip { get; set; }

    public virtual ICollection<Cargodetail> Cargodetails { get; set; } = new List<Cargodetail>();

    public virtual ICollection<Shippingroute> Shippingroutes { get; set; } = new List<Shippingroute>();
}
