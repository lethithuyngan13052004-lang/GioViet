using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Scaffolding.Internal;

namespace TimChuyenDi.Models;

public partial class TimchuyendiContext : DbContext
{
    public TimchuyendiContext()
    {
    }

    public TimchuyendiContext(DbContextOptions<TimchuyendiContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Behaviorlog> Behaviorlogs { get; set; }

    public virtual DbSet<Cargotype> Cargotypes { get; set; }

    public virtual DbSet<Chatmessage> Chatmessages { get; set; }

    public virtual DbSet<Chatsession> Chatsessions { get; set; }

    public virtual DbSet<District> Districts { get; set; }

    public virtual DbSet<Province> Provinces { get; set; }

    public virtual DbSet<Rating> Ratings { get; set; }

    public virtual DbSet<Report> Reports { get; set; }

    public virtual DbSet<RequestTripMatch> RequestTripMatches { get; set; }

    public virtual DbSet<Shiprequest> Shiprequests { get; set; }

    public virtual DbSet<Cargodetail> Cargodetails { get; set; }

    public virtual DbSet<Shippingroute> Shippingroutes { get; set; }

    public virtual DbSet<Station> Stations { get; set; }

    public virtual DbSet<Trip> Trips { get; set; }

    public virtual DbSet<TripStation> TripStations { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<Vehicle> Vehicles { get; set; }

    public virtual DbSet<VehicleCapacityConfig> VehicleCapacityConfigs { get; set; }

    public virtual DbSet<VehicleType> VehicleTypes { get; set; }

    public virtual DbSet<Ward> Wards { get; set; }

    public virtual DbSet<SystemConfig> SystemConfigs { get; set; }

    public virtual DbSet<TripType> TripTypes { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseMySql("server=localhost;database=timchuyendi;uid=root", Microsoft.EntityFrameworkCore.ServerVersion.Parse("10.4.32-mariadb"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_general_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Behaviorlog>(entity =>
        {
            entity.HasKey(e => e.BehaviorId).HasName("PRIMARY");

            entity.ToTable("behaviorlogs");

            entity.HasIndex(e => new { e.UserId, e.Action, e.Object, e.Value }, "unique_behavior").IsUnique();

            entity.Property(e => e.BehaviorId).HasColumnType("int(11)");
            entity.Property(e => e.Action).HasMaxLength(50);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.Object).HasMaxLength(50);
            entity.Property(e => e.UserId).HasColumnType("int(11)");
            entity.Property(e => e.Value).HasMaxLength(100);

            entity.HasOne(d => d.User).WithMany(p => p.Behaviorlogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("behaviorlogs_ibfk_1");
        });

        modelBuilder.Entity<Cargotype>(entity =>
        {
            entity.HasKey(e => e.CargoTypeId).HasName("PRIMARY");

            entity.ToTable("cargotypes");

            entity.Property(e => e.CargoTypeId).HasColumnType("int(11)");
            entity.Property(e => e.PriceMultiplier)
                .HasPrecision(3, 2)
                .HasDefaultValueSql("'1.00'");
            entity.Property(e => e.TypeName).HasMaxLength(255);
        });

        modelBuilder.Entity<Chatmessage>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("PRIMARY");

            entity.ToTable("chatmessages");

            entity.HasIndex(e => e.SenderId, "SenderId");

            entity.HasIndex(e => e.SessionId, "SessionId");

            entity.Property(e => e.MessageId).HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.EditedAt).HasColumnType("datetime");
            entity.Property(e => e.IsDeleted).HasDefaultValueSql("'0'");
            entity.Property(e => e.IsEdited).HasDefaultValueSql("'0'");
            entity.Property(e => e.IsImportant).HasDefaultValueSql("'0'");
            entity.Property(e => e.Message).HasColumnType("text");
            entity.Property(e => e.MessageType)
                .HasMaxLength(20)
                .HasDefaultValueSql("'text'");
            entity.Property(e => e.SenderId).HasColumnType("int(11)");
            entity.Property(e => e.SenderRole)
                .HasMaxLength(20)
                .HasComment("customer / driver / bot");
            entity.Property(e => e.SessionId).HasColumnType("int(11)");

            entity.HasOne(d => d.Sender).WithMany(p => p.Chatmessages)
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("chatmessages_ibfk_2");

            entity.HasOne(d => d.Session).WithMany(p => p.Chatmessages)
                .HasForeignKey(d => d.SessionId)
                .HasConstraintName("chatmessages_ibfk_1");
        });

        modelBuilder.Entity<Chatsession>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PRIMARY");

            entity.ToTable("chatsessions");

            entity.HasIndex(e => e.CustomerId, "CustomerId");

            entity.HasIndex(e => e.DriverId, "DriverId");

            entity.HasIndex(e => e.ReqId, "unique_req_session").IsUnique();

            entity.Property(e => e.SessionId).HasColumnType("int(11)");
            entity.Property(e => e.ClosedAt).HasColumnType("datetime");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerId).HasColumnType("int(11)");
            entity.Property(e => e.DriverId).HasColumnType("int(11)");
            entity.Property(e => e.ReqId).HasColumnType("int(11)");
            entity.Property(e => e.Status)
                .HasComment("0: Active, 1: Closed, 2: Cancelled")
                .HasColumnType("int(11)");

            entity.HasOne(d => d.Customer).WithMany(p => p.ChatsessionCustomers)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("chatsessions_ibfk_2");

            entity.HasOne(d => d.Driver).WithMany(p => p.ChatsessionDrivers)
                .HasForeignKey(d => d.DriverId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("chatsessions_ibfk_1");

            entity.HasOne(d => d.Req).WithOne(p => p.Chatsession)
                .HasForeignKey<Chatsession>(d => d.ReqId)
                .HasConstraintName("fk_chat_request");
        });

        modelBuilder.Entity<District>(entity =>
        {
            entity.HasKey(e => e.DistrictId).HasName("PRIMARY");

            entity.ToTable("districts");

            entity.HasIndex(e => e.ProvinceId, "ProvinceId");

            entity.Property(e => e.DistrictId)
                .ValueGeneratedNever()
                .HasColumnType("int(11)");
            entity.Property(e => e.DistrictName).HasMaxLength(100);
            entity.Property(e => e.ProvinceId).HasColumnType("int(11)");

            entity.HasOne(d => d.Province).WithMany(p => p.Districts)
                .HasForeignKey(d => d.ProvinceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("districts_ibfk_1");
        });

        modelBuilder.Entity<Province>(entity =>
        {
            entity.HasKey(e => e.ProvinceId).HasName("PRIMARY");

            entity.ToTable("provinces");

            entity.Property(e => e.ProvinceId)
                .ValueGeneratedNever()
                .HasColumnType("int(11)");
            entity.Property(e => e.ProvinceName).HasMaxLength(100);
        });

        modelBuilder.Entity<Rating>(entity =>
        {
            entity.HasKey(e => e.RatingId).HasName("PRIMARY");

            entity.ToTable("ratings");

            entity.HasIndex(e => e.CustomerId, "CustomerId");

            entity.HasIndex(e => e.ReqId, "fk_rating_request");

            entity.Property(e => e.RatingId).HasColumnType("int(11)");
            entity.Property(e => e.Comment).HasColumnType("text");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerId).HasColumnType("int(11)");
            entity.Property(e => e.ReqId).HasColumnType("int(11)");
            entity.Property(e => e.Score).HasColumnType("int(11)");

            entity.HasOne(d => d.Customer).WithMany(p => p.Ratings)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("ratings_ibfk_1");

            entity.HasOne(d => d.Req).WithMany(p => p.Ratings)
                .HasForeignKey(d => d.ReqId)
                .HasConstraintName("fk_rating_request");
        });

        modelBuilder.Entity<Report>(entity =>
        {
            entity.HasKey(e => e.ReportId).HasName("PRIMARY");

            entity.ToTable("reports");

            entity.HasIndex(e => e.DriverId, "DriverId");

            entity.HasIndex(e => e.ReporterId, "ReporterId");

            entity.HasIndex(e => e.ReqId, "ReqId");

            entity.Property(e => e.ReportId).HasColumnType("int(11)");
            entity.Property(e => e.Content).HasColumnType("text");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.DriverId).HasColumnType("int(11)");
            entity.Property(e => e.ReporterId).HasColumnType("int(11)");
            entity.Property(e => e.ReqId).HasColumnType("int(11)");
            entity.Property(e => e.ResolvedAt).HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasComment("0: Pending, 1: Reviewed, 2: Resolved, 3: Rejected")
                .HasColumnType("int(11)");

            entity.HasOne(d => d.Driver).WithMany(p => p.ReportDrivers)
                .HasForeignKey(d => d.DriverId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reports_ibfk_2");

            entity.HasOne(d => d.Reporter).WithMany(p => p.ReportReporters)
                .HasForeignKey(d => d.ReporterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("reports_ibfk_1");

            entity.HasOne(d => d.Req).WithMany(p => p.Reports)
                .HasForeignKey(d => d.ReqId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_report_request");
        });

        modelBuilder.Entity<RequestTripMatch>(entity =>
        {
            entity.HasKey(e => e.MatchId).HasName("PRIMARY");

            entity.ToTable("request_trip_match");

            entity.HasIndex(e => e.RequestId, "RequestId");

            entity.HasIndex(e => e.TripId, "TripId");

            entity.Property(e => e.MatchId).HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime");
            entity.Property(e => e.Note).HasColumnType("text");
            entity.Property(e => e.RequestId).HasColumnType("int(11)");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'Pending'")
                .HasComment("Pending / Accepted / Rejected / Cancelled");
            entity.Property(e => e.TripId).HasColumnType("int(11)");
            entity.Property(e => e.UpdatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("datetime");

            entity.HasOne(d => d.Request).WithMany(p => p.RequestTripMatches)
                .HasForeignKey(d => d.RequestId)
                .HasConstraintName("fk_match_request");

            entity.HasOne(d => d.Trip).WithMany(p => p.RequestTripMatches)
                .HasForeignKey(d => d.TripId)
                .HasConstraintName("request_trip_match_ibfk_2");
        });

        modelBuilder.Entity<Shiprequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("shiprequest");

            entity.HasIndex(e => e.UserId, "UserId");

            entity.HasIndex(e => e.TripId, "TripId");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("timestamp");
            entity.Property(e => e.Note).HasColumnType("text");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'0'")
                .HasComment("0: Pending, 1: Accepted, 2: Rejected, 3: Shipping, 4: Done")
                .HasColumnType("int(11)");
            entity.Property(e => e.TotalPrice)
                .HasDefaultValueSql("'0.00'")
                .HasPrecision(18, 2);
            entity.Property(e => e.TripId).HasColumnType("int(11)");
            entity.Property(e => e.UserId).HasColumnType("int(11)");

            entity.HasOne(d => d.User).WithMany(p => p.Shiprequests)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_shiprequest_user");

            entity.HasOne(d => d.Trip).WithMany(p => p.Shiprequests)
                .HasForeignKey(d => d.TripId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_shiprequest_trip");
        });

        modelBuilder.Entity<Cargodetail>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("cargodetail");

            entity.HasIndex(e => e.RequestId, "RequestId");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.Description).HasColumnType("text");
            entity.Property(e => e.Height).HasPrecision(10, 2);
            entity.Property(e => e.Length).HasPrecision(10, 2);
            entity.Property(e => e.RequestId).HasColumnType("int(11)");
            entity.Property(e => e.Weight).HasPrecision(10, 2);
            entity.Property(e => e.Width).HasPrecision(10, 2);

            entity.HasOne(d => d.Request).WithMany(p => p.Cargodetails)
                .HasForeignKey(d => d.RequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_cargodetail_request");
        });

        modelBuilder.Entity<Shippingroute>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("shippingroute");

            entity.HasIndex(e => e.RequestId, "RequestId");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.DeliveryAddress).HasMaxLength(500);
            entity.Property(e => e.FromStationId).HasColumnType("int(11)");
            entity.Property(e => e.FromProvinceId).HasColumnType("int(11)");
            entity.Property(e => e.ToProvinceId).HasColumnType("int(11)");
            entity.Property(e => e.Lat).HasPrecision(10, 8);
            entity.Property(e => e.Lng).HasPrecision(11, 8);
            entity.Property(e => e.PickupAddress).HasMaxLength(500);
            entity.Property(e => e.PickupType).HasColumnType("int(11)");
            entity.Property(e => e.DeliveryType).HasColumnType("int(11)");
            entity.Property(e => e.ReceiverName).HasMaxLength(100);
            entity.Property(e => e.ReceiverPhone).HasMaxLength(15);
            entity.Property(e => e.RequestId).HasColumnType("int(11)");
            entity.Property(e => e.SenderPhone).HasMaxLength(15);
            entity.Property(e => e.ToStationId).HasColumnType("int(11)");

            entity.HasOne(d => d.Request).WithMany(p => p.Shippingroutes)
                .HasForeignKey(d => d.RequestId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_shippingroute_request");
        });

        modelBuilder.Entity<Station>(entity =>
        {
            entity.HasKey(e => e.StationId).HasName("PRIMARY");

            entity.ToTable("stations");

            entity.HasIndex(e => e.DistrictId, "fk_station_district");

            entity.HasIndex(e => e.ProvinceId, "fk_station_province");

            entity.HasIndex(e => e.WardId, "fk_station_ward");

            entity.Property(e => e.StationId).HasColumnType("int(11)");
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.DistrictId).HasColumnType("int(11)");
            entity.Property(e => e.Latitude).HasPrecision(10, 8);
            entity.Property(e => e.Longitude).HasPrecision(11, 8);
            entity.Property(e => e.ProvinceId).HasColumnType("int(11)");
            entity.Property(e => e.StationName).HasMaxLength(255);
            entity.Property(e => e.WardId).HasColumnType("int(11)");

            entity.HasOne(d => d.District).WithMany(p => p.Stations)
                .HasForeignKey(d => d.DistrictId)
                .HasConstraintName("fk_station_district");

            entity.HasOne(d => d.Province).WithMany(p => p.Stations)
                .HasForeignKey(d => d.ProvinceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_station_province");

            entity.HasOne(d => d.Ward).WithMany(p => p.Stations)
                .HasForeignKey(d => d.WardId)
                .HasConstraintName("fk_station_ward");
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.HasKey(e => e.TripId).HasName("PRIMARY");

            entity.ToTable("trips");

            entity.HasIndex(e => e.DriverId, "DriverId");

            entity.HasIndex(e => e.FromStation, "FromStation");

            entity.HasIndex(e => e.ToStation, "ToStation");

            entity.HasIndex(e => e.VehicleId, "VehicleId");

            entity.Property(e => e.TripId).HasColumnType("int(11)");
            entity.Property(e => e.ArrivalTime).HasColumnType("datetime");
            entity.Property(e => e.AvaiCapacityKg).HasColumnType("int(11)");
            entity.Property(e => e.AvaiCapacityM3).HasColumnType("int(11)");
            entity.Property(e => e.BasePrice).HasPrecision(18, 2).HasDefaultValueSql("'0.00'").HasComment("Giá mở đầu do tài xế nhập");
            entity.Property(e => e.Distance).HasPrecision(10, 2).HasComment("Khoảng cách (km)");
            entity.Property(e => e.DriverId).HasColumnType("int(11)");
            entity.Property(e => e.FromStation).HasColumnType("int(11)");
            entity.Property(e => e.RouteType)
                .HasDefaultValueSql("'1'")
                .HasComment("1: Direct, 2: Shared")
                .HasColumnType("int(11)");
            entity.Property(e => e.StartTime).HasColumnType("datetime");
            entity.Property(e => e.ToStation).HasColumnType("int(11)");
            entity.Property(e => e.VehicleId).HasColumnType("int(11)");
            entity.Property(e => e.TotalPrice).HasPrecision(10, 2);
            entity.Property(e => e.PlatformFee).HasPrecision(10, 2);
            entity.Property(e => e.DriverEarning).HasPrecision(10, 2);

            entity.HasOne(d => d.Driver).WithMany(p => p.TripsNavigation)
                .HasForeignKey(d => d.DriverId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trips_ibfk_1");

            entity.HasOne(d => d.FromStationNavigation).WithMany(p => p.TripFromStationNavigations)
                .HasForeignKey(d => d.FromStation)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trips_ibfk_3");

            entity.HasOne(d => d.ToStationNavigation).WithMany(p => p.TripToStationNavigations)
                .HasForeignKey(d => d.ToStation)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trips_ibfk_4");

            entity.HasOne(d => d.Vehicle).WithMany(p => p.Trips)
                .HasForeignKey(d => d.VehicleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trips_ibfk_2");

            entity.HasOne(d => d.RouteTypeNavigation).WithMany(p => p.Trips)
                .HasForeignKey(d => d.RouteType)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trips_ibfk_5");
        });

        modelBuilder.Entity<TripStation>(entity =>
        {
            entity.HasKey(e => e.TripStationId).HasName("PRIMARY");

            entity.ToTable("trip_stations");

            entity.HasIndex(e => e.StationId, "StationId");

            entity.HasIndex(e => e.TripId, "TripId");

            entity.Property(e => e.TripStationId).HasColumnType("int(11)");
            entity.Property(e => e.EstArrivalTime).HasColumnType("datetime");
            entity.Property(e => e.ArrivalTime).HasColumnType("datetime");
            entity.Property(e => e.DistanceFromPrev).HasColumnType("double").HasDefaultValue(0.0);
            entity.Property(e => e.StationId).HasColumnType("int(11)");
            entity.Property(e => e.StopOrder).HasColumnType("int(11)");
            entity.Property(e => e.TripId).HasColumnType("int(11)");

            entity.HasOne(d => d.Station).WithMany(p => p.TripStations)
                .HasForeignKey(d => d.StationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("trip_stations_ibfk_2");

            entity.HasOne(d => d.Trip).WithMany(p => p.TripStations)
                .HasForeignKey(d => d.TripId)
                .HasConstraintName("trip_stations_ibfk_1");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PRIMARY");

            entity.ToTable("users");

            entity.HasIndex(e => e.Phone, "Phone").IsUnique();

            entity.Property(e => e.UserId).HasColumnType("int(11)");
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasComment("1: Active, 0: Banned");
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.PasswordDemo)
                .HasMaxLength(255)
                .HasComment("Dùng test");
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Role)
                .HasComment("1: Admin, 2: Customer, 3: Driver")
                .HasColumnType("int(11)");

            entity.HasMany(d => d.Trips).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "Savedroute",
                    r => r.HasOne<Trip>().WithMany()
                        .HasForeignKey("TripId")
                        .HasConstraintName("savedroutes_ibfk_2"),
                    l => l.HasOne<User>().WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("savedroutes_ibfk_1"),
                    j =>
                    {
                        j.HasKey("UserId", "TripId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                        j.ToTable("savedroutes");
                        j.HasIndex(new[] { "TripId" }, "TripId");
                        j.IndexerProperty<int>("UserId").HasColumnType("int(11)");
                        j.IndexerProperty<int>("TripId").HasColumnType("int(11)");
                    });
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.HasKey(e => e.VehicleId).HasName("PRIMARY");

            entity.ToTable("vehicles");

            entity.HasIndex(e => e.DriverId, "DriverId");

            entity.HasIndex(e => e.PlateNumber, "PlateNumber").IsUnique();

            entity.HasIndex(e => e.VehicleTypeId, "fk_vehicle_type");

            entity.Property(e => e.VehicleId).HasColumnType("int(11)");
            entity.Property(e => e.CapacityKg).HasColumnType("int(11)");
            entity.Property(e => e.CapacityM3).HasColumnType("int(11)");
            entity.Property(e => e.DriverId).HasColumnType("int(11)");
            entity.Property(e => e.PlateNumber).HasMaxLength(50);
            entity.Property(e => e.VehicleTypeId).HasColumnType("int(11)");

            entity.HasOne(d => d.Driver).WithMany(p => p.Vehicles)
                .HasForeignKey(d => d.DriverId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("vehicles_ibfk_1");

            entity.HasOne(d => d.VehicleType).WithMany(p => p.Vehicles)
                .HasForeignKey(d => d.VehicleTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_vehicle_type");
        });

        modelBuilder.Entity<VehicleType>(entity =>
        {
            entity.HasKey(e => e.VehicleTypeId).HasName("PRIMARY");

            entity.ToTable("vehicle_types");

            entity.Property(e => e.VehicleTypeId).HasColumnType("int(11)");
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.TypeName).HasMaxLength(100);

            entity.HasMany(d => d.CargoTypes).WithMany(p => p.VehicleTypes)
                .UsingEntity<Dictionary<string, object>>(
                    "VehicletypeCargotype",
                    r => r.HasOne<Cargotype>().WithMany()
                        .HasForeignKey("CargoTypeId")
                        .HasConstraintName("vehicletype_cargotype_ibfk_2"),
                    l => l.HasOne<VehicleType>().WithMany()
                        .HasForeignKey("VehicleTypeId")
                        .HasConstraintName("vehicletype_cargotype_ibfk_1"),
                    j =>
                    {
                        j.HasKey("VehicleTypeId", "CargoTypeId")
                            .HasName("PRIMARY")
                            .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });
                        j.ToTable("vehicletype_cargotype");
                        j.HasIndex(new[] { "CargoTypeId" }, "CargoTypeId");
                        j.IndexerProperty<int>("VehicleTypeId").HasColumnType("int(11)");
                        j.IndexerProperty<int>("CargoTypeId").HasColumnType("int(11)");
                    });
        });

        modelBuilder.Entity<Ward>(entity =>
        {
            entity.HasKey(e => e.WardId).HasName("PRIMARY");

            entity.ToTable("wards");

            entity.HasIndex(e => e.DistrictId, "DistrictId");

            entity.Property(e => e.WardId)
                .ValueGeneratedNever()
                .HasColumnType("int(11)");
            entity.Property(e => e.DistrictId).HasColumnType("int(11)");
            entity.Property(e => e.WardName).HasMaxLength(100);

            entity.HasOne(d => d.District).WithMany(p => p.Wards)
                .HasForeignKey(d => d.DistrictId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("wards_ibfk_1");
        });

        modelBuilder.Entity<VehicleCapacityConfig>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("vehicle_capacity_config");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.EstimatedVolume).HasColumnType("float");
            entity.Property(e => e.MaxWeight).HasColumnType("int(11)");
            entity.Property(e => e.MinWeight).HasColumnType("int(11)");
            entity.Property(e => e.VehicleTypeId).HasColumnType("int(11)");
        });

        modelBuilder.Entity<SystemConfig>(entity =>
        {
            entity.HasKey(e => e.KeyName).HasName("PRIMARY");
            entity.ToTable("system_config");
            entity.Property(e => e.KeyName).HasMaxLength(50);
            entity.Property(e => e.Value).HasPrecision(10, 2);
        });

        modelBuilder.Entity<TripType>(entity =>
        {
            entity.HasKey(e => e.IdType).HasName("PRIMARY");
            entity.ToTable("trip_types");
            entity.Property(e => e.IdType).HasColumnType("int(11)");
            entity.Property(e => e.Type).HasMaxLength(50);
            entity.Property(e => e.Multiplier).HasPrecision(10, 2);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
