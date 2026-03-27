using System.ComponentModel.DataAnnotations;

namespace TimChuyenDi.Models;

public partial class SystemConfig
{
    [Key]
    public string KeyName { get; set; } = null!;

    public decimal Value { get; set; }
}
