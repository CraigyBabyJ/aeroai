using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace AtcNavDataDemo.SimBrief;

public sealed class SimBriefClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public SimBriefClient(HttpClient? httpClient = null)
    {
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
    }

    /// <summary>
    /// Fetches the latest OFP for the given SimBrief username or pilot ID.
    /// Returns null if the request fails or the response is malformed.
    /// </summary>
    public async Task<FlightPlan?> FetchLatestFlightPlanAsync(string usernameOrPilotId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usernameOrPilotId))
            throw new ArgumentException("SimBrief username or pilot ID must be provided.", nameof(usernameOrPilotId));

        usernameOrPilotId = usernameOrPilotId.Trim();

        // Try JSON format first (newer API)
        string url;
        if (usernameOrPilotId.All(char.IsDigit))
        {
            // Pilot ID
            url = $"https://www.simbrief.com/api/xml.fetcher.php?userid={usernameOrPilotId}&json=1";
        }
        else
        {
            // Username
            url = $"https://www.simbrief.com/api/xml.fetcher.php?username={Uri.EscapeDataString(usernameOrPilotId)}&json=1";
        }

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"SimBrief API returned status {response.StatusCode}: {errorContent}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var contentString = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(contentString))
            {
                throw new InvalidOperationException("SimBrief API returned empty response");
            }

            // Try to parse as JSON first if json parameter is in URL
            if (url.Contains("json=1"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(contentString);
                    return ParseJsonResponse(doc.RootElement);
                }
                catch (JsonException)
                {
                    // If JSON parsing fails, try XML
                    return ParseXmlResponse(contentString);
                }
            }
            else
            {
                // Parse as XML (default format)
                return ParseXmlResponse(contentString);
            }
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw HTTP errors
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to fetch or parse SimBrief flight plan: {ex.Message}", ex);
        }
    }

    private FlightPlan? ParseJsonResponse(JsonElement root)
    {
        // JSON structure - try both v1 and v2 formats
        var originIcao = string.Empty;
        var destIcao = string.Empty;
        var altIcao = string.Empty;
        string callsign = string.Empty;
        string flightNumber = string.Empty;
        string route = string.Empty;
        int cruiseFl = 0;

        // Try v2 format first (nested structure)
        if (root.TryGetProperty("origin", out var originElem))
        {
            if (originElem.ValueKind == JsonValueKind.Object)
            {
                // It's an object, try to get icao_code
                if (originElem.TryGetProperty("icao_code", out var oIcaoElem))
                {
                    originIcao = oIcaoElem.ValueKind == JsonValueKind.String 
                        ? oIcaoElem.GetString() ?? string.Empty 
                        : string.Empty;
                }
            }
            else if (originElem.ValueKind == JsonValueKind.String)
            {
                // It's a string directly
                originIcao = originElem.GetString() ?? string.Empty;
            }
        }

        if (root.TryGetProperty("destination", out var destElem))
        {
            if (destElem.ValueKind == JsonValueKind.Object)
            {
                // It's an object, try to get icao_code
                if (destElem.TryGetProperty("icao_code", out var dIcaoElem))
                {
                    destIcao = dIcaoElem.ValueKind == JsonValueKind.String 
                        ? dIcaoElem.GetString() ?? string.Empty 
                        : string.Empty;
                }
            }
            else if (destElem.ValueKind == JsonValueKind.String)
            {
                // It's a string directly
                destIcao = destElem.GetString() ?? string.Empty;
            }
        }

        if (root.TryGetProperty("alternate", out var altElem))
        {
            if (altElem.ValueKind == JsonValueKind.Object)
            {
                // It's an object, try to get icao_code
                if (altElem.TryGetProperty("icao_code", out var aIcaoElem))
                {
                    altIcao = aIcaoElem.ValueKind == JsonValueKind.String 
                        ? aIcaoElem.GetString() ?? string.Empty 
                        : string.Empty;
                }
            }
            else if (altElem.ValueKind == JsonValueKind.String)
            {
                // It's a string directly
                altIcao = altElem.GetString() ?? string.Empty;
            }
        }

        var waypoints = new List<string>();

        if (root.TryGetProperty("general", out var gen) && gen.ValueKind == JsonValueKind.Object)
        {
            if (gen.TryGetProperty("callsign", out var cs) && cs.ValueKind == JsonValueKind.String)
                callsign = cs.GetString() ?? string.Empty;

            if (gen.TryGetProperty("flight_number", out var fn) && fn.ValueKind == JsonValueKind.String)
                flightNumber = fn.GetString() ?? string.Empty;

            if (gen.TryGetProperty("route", out var r) && r.ValueKind == JsonValueKind.String)
                route = r.GetString() ?? string.Empty;

            if (gen.TryGetProperty("initial_altitude", out var altElem2))
            {
                string? altStr = null;
                if (altElem2.ValueKind == JsonValueKind.String)
                {
                    altStr = altElem2.GetString();
                }
                else if (altElem2.ValueKind == JsonValueKind.Number)
                {
                    altStr = altElem2.GetInt32().ToString();
                }
                
                if (altStr != null && int.TryParse(altStr, out var feet) && feet > 0)
                {
                    cruiseFl = feet / 100;
                }
            }
        }
        else
        {
            // Try flat structure
            if (root.TryGetProperty("callsign", out var cs) && cs.ValueKind == JsonValueKind.String)
                callsign = cs.GetString() ?? string.Empty;

            if (root.TryGetProperty("flight_number", out var fn) && fn.ValueKind == JsonValueKind.String)
                flightNumber = fn.GetString() ?? string.Empty;

            if (root.TryGetProperty("route", out var r) && r.ValueKind == JsonValueKind.String)
                route = r.GetString() ?? string.Empty;
        }

        // Parse navlog waypoints - this is the key data we need for enroute routing
        if (root.TryGetProperty("navlog", out var navlog) && navlog.ValueKind == JsonValueKind.Object)
        {
            if (navlog.TryGetProperty("fix", out var fixes) && fixes.ValueKind == JsonValueKind.Array)
            {
                foreach (var fix in fixes.EnumerateArray())
                {
                    if (fix.ValueKind == JsonValueKind.Object)
                    {
                        // Get waypoint identifier
                        if (fix.TryGetProperty("ident", out var ident) && ident.ValueKind == JsonValueKind.String)
                        {
                            var wpId = ident.GetString();
                            if (!string.IsNullOrWhiteSpace(wpId))
                            {
                                // Check if this is an enroute waypoint (not SID/STAR)
                                // We can check the via_airway property or flight_rules
                                var isEnroute = true;
                                
                                // Skip if it's marked as SID or STAR
                                if (fix.TryGetProperty("via_airway", out var via) && via.ValueKind == JsonValueKind.String)
                                {
                                    var viaStr = via.GetString() ?? "";
                                    // SID/STAR identifiers are typically short codes
                                    if (viaStr.Length <= 6 && (viaStr.Contains("SID") || viaStr.Contains("STAR")))
                                        isEnroute = false;
                                }
                                
                                if (isEnroute)
                                {
                                    waypoints.Add(wpId.ToUpperInvariant());
                                }
                            }
                        }
                    }
                }
            }
        }

        // Fallback: if no navlog, try to parse route string
        if (waypoints.Count == 0 && !string.IsNullOrWhiteSpace(route))
        {
            // Simple parsing of route string
            var tokens = route.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var ignoreTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "DCT", "DIRECT", "SID", "STAR", "VIA", "TO", "FROM"
            };

            foreach (var token in tokens)
            {
                var trimmed = token.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !ignoreTokens.Contains(trimmed))
                {
                    waypoints.Add(trimmed.ToUpperInvariant());
                }
            }
        }

        if (string.IsNullOrWhiteSpace(originIcao) || string.IsNullOrWhiteSpace(destIcao))
        {
            return null;
        }

        return new FlightPlan
        {
            OriginIcao = originIcao.ToUpperInvariant(),
            DestinationIcao = destIcao.ToUpperInvariant(),
            AlternateIcao = altIcao.ToUpperInvariant(),
            Callsign = callsign.ToUpperInvariant(),
            FlightNumber = flightNumber,
            Route = route,
            CruiseFlightLevel = cruiseFl,
            WaypointIdentifiers = waypoints
        };
    }

    private FlightPlan? ParseXmlResponse(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);
        var root = doc.Root;
        if (root == null)
            return null;

        var originIcao = root.Element("origin")?.Element("icao_code")?.Value ?? 
                        root.Element("origin")?.Value ?? string.Empty;
        var destIcao = root.Element("destination")?.Element("icao_code")?.Value ?? 
                      root.Element("destination")?.Value ?? string.Empty;
        var altIcao = root.Element("alternate")?.Element("icao_code")?.Value ?? 
                     root.Element("alternate")?.Value ?? string.Empty;

        var general = root.Element("general");
        var callsign = general?.Element("callsign")?.Value ?? string.Empty;
        var flightNumber = general?.Element("flight_number")?.Value ?? string.Empty;
        var route = general?.Element("route")?.Value ?? string.Empty;
        
        int cruiseFl = 0;
        var initialAlt = general?.Element("initial_altitude")?.Value;
        if (int.TryParse(initialAlt, out var feet) && feet > 0)
        {
            cruiseFl = feet / 100;
        }

        if (string.IsNullOrWhiteSpace(originIcao) || string.IsNullOrWhiteSpace(destIcao))
        {
            return null;
        }

        return new FlightPlan
        {
            OriginIcao = originIcao.ToUpperInvariant(),
            DestinationIcao = destIcao.ToUpperInvariant(),
            AlternateIcao = altIcao.ToUpperInvariant(),
            Callsign = callsign.ToUpperInvariant(),
            FlightNumber = flightNumber,
            Route = route,
            CruiseFlightLevel = cruiseFl
        };
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
