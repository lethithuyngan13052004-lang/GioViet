using Microsoft.EntityFrameworkCore;
using TimChuyenDi.Models;
using System;
using System.Linq;
using System.Collections.Generic;

var context = new TimchuyendiContext();
int fromId = 1; // Hanoi
int toId = 48; // Da Nang

var query = context.Trips
    .Include(t => t.FromStationNavigation).ThenInclude(s => s.Province)
    .Include(t => t.ToStationNavigation).ThenInclude(s => s.Province)
    .Include(t => t.TripStations).ThenInclude(ts => ts.Station).ThenInclude(s => s.Province)
    .Where(t => new[] { 0, 1 }.Contains(t.Status));

query = query.Where(t => t.StartTime > DateTime.Now.AddHours(-1));

var allTrips = query.OrderBy(t => t.StartTime).ToList();

var filteredTrips = allTrips.Where(t => {
    var route = new List<int> { t.FromStationNavigation.ProvinceId };
    route.AddRange(t.TripStations.OrderBy(ts => ts.StopOrder).Select(ts => ts.Station.ProvinceId));
    route.Add(t.ToStationNavigation.ProvinceId);
    
    int fIdx = route.IndexOf(fromId);
    int tIdx = route.LastIndexOf(toId);
    
    return fIdx != -1 && tIdx != -1 && fIdx < tIdx;
}).ToList();

Console.WriteLine($"Total trips after basic query: {allTrips.Count}");
Console.WriteLine($"Filtered trips (from {fromId} to {toId}): {filteredTrips.Count}");

foreach (var t in filteredTrips) {
    Console.WriteLine($"Found Trip: {t.TripId}");
}

// specifically check trip 135
var trip135 = allTrips.FirstOrDefault(t => t.TripId == 135);
if (trip135 != null) {
    var route = new List<int> { trip135.FromStationNavigation.ProvinceId };
    route.AddRange(trip135.TripStations.OrderBy(ts => ts.StopOrder).Select(ts => ts.Station.ProvinceId));
    route.Add(trip135.ToStationNavigation.ProvinceId);
    Console.WriteLine($"Trip 135 route: {string.Join(", ", route)}");
    int fIdx = route.IndexOf(fromId);
    int tIdx = route.LastIndexOf(toId);
    Console.WriteLine($"Trip 135 fIdx: {fIdx}, tIdx: {tIdx}");
} else {
    Console.WriteLine("Trip 135 not in allTrips. Let's find why.");
    var t135 = context.Trips.FirstOrDefault(t => t.TripId == 135);
    if (t135 != null) {
        Console.WriteLine($"Trip 135 exists in DB. Status: {t135.Status}, StartTime: {t135.StartTime}");
        Console.WriteLine($"FromStation: {t135.FromStation}, ToStation: {t135.ToStation}");
    } else {
        Console.WriteLine("Trip 135 DOES NOT EXIST IN DB!");
    }
}
