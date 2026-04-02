using Microsoft.EntityFrameworkCore;
using TimChuyenDi.Models;
using System;
using System.Linq;

// Manual setup of DbContext if needed, but usually we can just use the existing one
var context = new TimchuyendiContext();

var now = DateTime.Now;
Console.WriteLine($"Current System Time: {now}");

var trips = context.Trips
    .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
    .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
    .Include(t => t.TripStations).ThenInclude(ts => ts.Station).ThenInclude(s => s.Province)
    .ToList();

Console.WriteLine($"Total trips in database: {trips.Count}");

var activeTrips = trips.Where(t => t.StartTime > now).ToList();
Console.WriteLine($"Total ACTIVE trips (StartTime > now): {activeTrips.Count}");

var hnLc = trips.Where(t => 
    (t.FromStationNavigation.Province.ProvinceName.Contains("Hà Nội") || t.TripStations.Any(ts => ts.Station.Province.ProvinceName.Contains("Hà Nội"))) &&
    (t.ToStationNavigation.Province.ProvinceName.Contains("Lào Cai") || t.TripStations.Any(ts => ts.Station.Province.ProvinceName.Contains("Lào Cai")))
).ToList();

Console.WriteLine($"\n--- HN to LC Trips (Any time) ---");
foreach (var t in hnLc)
{
    Console.WriteLine($"ID: {t.TripId} | {t.FromStationNavigation.Province.ProvinceName} -> {t.ToStationNavigation.Province.ProvinceName} | Start: {t.StartTime} | Capacity: {t.AvaiCapacityKg}kg");
}

var provinces = context.Provinces.ToList();
Console.WriteLine($"\n--- Provinces List ---");
foreach (var p in provinces)
{
    Console.WriteLine($"ID: {p.ProvinceId} | Name: {p.ProvinceName}");
}
