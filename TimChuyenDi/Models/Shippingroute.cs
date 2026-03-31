using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class Shippingroute
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    public string? SenderPhone { get; set; }

    public int? FromProvinceId { get; set; }
    public int? ToProvinceId { get; set; }
    public int PickupType { get; set; }
    public int DeliveryType { get; set; }

    public string? PickupAddress { get; set; }

    public decimal? Lat { get; set; }

    public decimal? Lng { get; set; }

    public int? FromStationId { get; set; }

    public string? ReceiverName { get; set; }

    public string? ReceiverPhone { get; set; }

    public int? ToStationId { get; set; }

    public string? DeliveryAddress { get; set; }

    public virtual Shiprequest Request { get; set; } = null!;
    public virtual Station? FromStation { get; set; }
    public virtual Station? ToStation { get; set; }
}

