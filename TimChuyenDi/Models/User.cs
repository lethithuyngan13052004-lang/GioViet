using System;
using System.Collections.Generic;

namespace TimChuyenDi.Models;

public partial class User
{
    public int UserId { get; set; }

    public string Name { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public string? Email { get; set; }
    
    public DateTime CreatedAt { get; set; }

    public string PasswordHash { get; set; } = null!;

    /// <summary>
    /// Dùng test
    /// </summary>
    public string? PasswordDemo { get; set; }

    /// <summary>
    /// 1: Admin, 2: Customer, 3: Driver
    /// </summary>
    public int Role { get; set; }

    /// <summary>
    /// 1: Active, 0: Banned
    /// </summary>
    public bool? IsActive { get; set; }

    public string? LockReason { get; set; }

    public virtual ICollection<Behaviorlog> Behaviorlogs { get; set; } = new List<Behaviorlog>();

    public virtual ICollection<Chatmessage> Chatmessages { get; set; } = new List<Chatmessage>();

    public virtual ICollection<Chatsession> ChatsessionCustomers { get; set; } = new List<Chatsession>();

    public virtual ICollection<Chatsession> ChatsessionDrivers { get; set; } = new List<Chatsession>();

    public virtual ICollection<Rating> Ratings { get; set; } = new List<Rating>();

    public virtual ICollection<Report> ReportDrivers { get; set; } = new List<Report>();

    public virtual ICollection<Report> ReportReporters { get; set; } = new List<Report>();

    public virtual ICollection<Shiprequest> Shiprequests { get; set; } = new List<Shiprequest>();

    public virtual ICollection<Trip> TripsNavigation { get; set; } = new List<Trip>();

    public virtual ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();

    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();
}
