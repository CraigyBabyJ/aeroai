using System.Net.Http;
using System.Text.Json;
using AeroAI.Models;

namespace AtcNavDataDemo.Weather;

public sealed class CheckWxClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly bool _ownsClient;

    public CheckWxClient(string apiKey, HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        
        if (httpClient is null)
        {
            _httpClient = new HttpClient();
            _ownsClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsClient = false;
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
    }

    /// <summary>
    /// Fetches current METAR weather for the given airport ICAO.
    /// </summary>
    public async Task<WeatherInfo?> GetWeatherAsync(string airportIcao, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(airportIcao))
            throw new ArgumentException("Airport ICAO must be provided.", nameof(airportIcao));

        airportIcao = airportIcao.Trim().ToUpperInvariant();

        try
        {
            var url = $"https://api.checkwx.com/metar/{airportIcao}/decoded";
            
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                // If API fails, return null (caller can use defaults)
                return null;
            }

            var contentString = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (string.IsNullOrWhiteSpace(contentString))
                return null;

            using var doc = JsonDocument.Parse(contentString);
            var root = doc.RootElement;

            // CheckWx returns data in a "data" array
            if (!root.TryGetProperty("data", out var dataArray) || 
                dataArray.ValueKind != JsonValueKind.Array ||
                dataArray.GetArrayLength() == 0)
            {
                return null;
            }

            var metar = dataArray[0];
            
            // Parse METAR data
            var windDir = 0;
            var windSpeed = 0;
            var visibility = 10000; // meters, default
            var ceiling = 0; // feet AGL
            var isIfr = false;

            // Wind
            if (metar.TryGetProperty("wind", out var wind))
            {
                if (wind.TryGetProperty("degrees", out var windDeg))
                {
                    if (windDeg.ValueKind == JsonValueKind.Number)
                        windDir = windDeg.GetInt32();
                    else if (windDeg.ValueKind == JsonValueKind.String && int.TryParse(windDeg.GetString(), out var dir))
                        windDir = dir;
                }

                if (wind.TryGetProperty("speed_kts", out var windSpd))
                {
                    if (windSpd.ValueKind == JsonValueKind.Number)
                        windSpeed = windSpd.GetInt32();
                    else if (windSpd.ValueKind == JsonValueKind.String && int.TryParse(windSpd.GetString(), out var spd))
                        windSpeed = spd;
                }
            }

            // Visibility
            if (metar.TryGetProperty("visibility", out var vis))
            {
                if (vis.TryGetProperty("meters", out var visMeters))
                {
                    if (visMeters.ValueKind == JsonValueKind.Number)
                        visibility = (int)visMeters.GetDouble();
                    else if (visMeters.ValueKind == JsonValueKind.String && int.TryParse(visMeters.GetString(), out var visM))
                        visibility = visM;
                }
                else if (vis.TryGetProperty("meters_float", out var visFloat))
                {
                    if (visFloat.ValueKind == JsonValueKind.Number)
                        visibility = (int)visFloat.GetDouble();
                }
            }

            // Ceiling (from clouds)
            if (metar.TryGetProperty("clouds", out var clouds) && clouds.ValueKind == JsonValueKind.Array)
            {
                foreach (var cloud in clouds.EnumerateArray())
                {
                    if (cloud.TryGetProperty("code", out var code) && 
                        code.GetString() is "BKN" or "OVC" or "OVX")
                    {
                        if (cloud.TryGetProperty("feet_agl", out var feetAgl))
                        {
                            int cloudBase = 0;
                            if (feetAgl.ValueKind == JsonValueKind.Number)
                                cloudBase = feetAgl.GetInt32();
                            else if (feetAgl.ValueKind == JsonValueKind.String && int.TryParse(feetAgl.GetString(), out var cb))
                                cloudBase = cb;

                            if (cloudBase > 0 && (ceiling == 0 || cloudBase < ceiling))
                                ceiling = cloudBase;
                        }
                    }
                }
            }

            // IFR conditions
            if (metar.TryGetProperty("flight_category", out var category))
            {
                var cat = category.GetString()?.ToUpperInvariant() ?? "";
                isIfr = cat is "IFR" or "LIFR";
            }
            else
            {
                // Heuristic: IFR if visibility < 3 miles or ceiling < 1000 ft
                isIfr = visibility < 4800 || ceiling > 0 && ceiling < 1000;
            }

            return new WeatherInfo
            {
                AirportIcao = airportIcao,
                WindDirectionDegrees = windDir,
                WindSpeedKnots = windSpeed,
                VisibilityMeters = visibility,
                CeilingFeet = ceiling,
                IsIfr = isIfr,
                IsLowVisibility = visibility < 800 || (ceiling > 0 && ceiling < 200)
            };
        }
        catch
        {
            // If parsing fails, return null
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}

