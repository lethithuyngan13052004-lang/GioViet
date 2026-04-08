using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TimChuyenDi.Services
{
    public class RoutingService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RoutingService> _logger;

        public RoutingService(HttpClient httpClient, ILogger<RoutingService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<(double distanceKm, double durationSeconds)> GetRouteAsync(List<(double lat, double lng)> coordinates)
        {
            if (coordinates == null || coordinates.Count < 2)
                return (0, 0);

            try
            {
                var waypoints = string.Join(";", coordinates.ConvertAll(c => $"{c.lng.ToString(System.Globalization.CultureInfo.InvariantCulture)},{c.lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
                var url = $"https://router.project-osrm.org/route/v1/driving/{waypoints}?overview=false";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"OSRM Routing failed with status {response.StatusCode}");
                    return (0, 0);
                }

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.GetProperty("code").GetString() == "Ok")
                {
                    var routes = root.GetProperty("routes");
                    if (routes.GetArrayLength() > 0)
                    {
                        var route = routes[0];
                        double distance = route.GetProperty("distance").GetDouble(); // meters
                        double duration = route.GetProperty("duration").GetDouble(); // seconds
                        return (distance / 1000.0, duration);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling OSRM Routing API");
            }

            return (0, 0);
        }

        public async Task<(double? lat, double? lng, string? displayName)> GeocodeAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return (null, null, null);

            try
            {
                // Nominatim yêu cầu User-Agent hợp lệ
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GioViet-Chatbot/1.0");
                
                var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(address)}&format=json&countrycodes=vn&limit=1";
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.GetArrayLength() > 0)
                    {
                        var first = doc.RootElement[0];
                        double lat = double.Parse(first.GetProperty("lat").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                        double lng = double.Parse(first.GetProperty("lon").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                        string displayName = first.GetProperty("display_name").GetString()!;
                        return (lat, lng, displayName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Geocoding failed for address: {address}");
            }
            return (null, null, null);
        }
    }
}
