using Microsoft.EntityFrameworkCore;
using TimChuyenDi.Models;
using System;
using System.Linq;

var context = new TimchuyendiContext();

var now = DateTime.Now;
var trips = context.Trips
    .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
    .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
    .Where(t => t.StartTime > now)
    .ToList();

Console.WriteLine($"Total active trips: {trips.Count}");

foreach (var t in trips)
{
    Console.WriteLine($"Trip #{t.TripId}: {t.FromStationNavigation.Province.ProvinceName} -> {t.ToStationNavigation.Province.ProvinceName} ({t.StartTime})");
}

var hnLc = trips.Where(t => 
    (t.FromStationNavigation.Province.ProvinceName.Contains("Hà Nội") && t.ToStationNavigation.Province.ProvinceName.Contains("Lào Cai")) ||
    (t.FromStationNavigation.Province.ProvinceName.Contains("Lào Cai") && t.ToStationNavigation.Province.ProvinceName.Contains("Hà Nội"))
).ToList();

Console.WriteLine($"Found {hnLc.Count} HN-LC trips.");
