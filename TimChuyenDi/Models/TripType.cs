using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class TripType
{
    public int IdType { get; set; }
    public string Type { get; set; } = null!;
    public decimal Multiplier { get; set; }

    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
