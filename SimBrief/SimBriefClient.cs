using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using System.Text.RegularExpressions;

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
        var originName = string.Empty;
        string? originMetar = null;
        var destIcao = string.Empty;
        var destName = string.Empty;
        string? destMetar = null;
        var altIcao = string.Empty;
        var plannedDepRunway = string.Empty;
        var plannedArrRunway = string.Empty;
        var plannedSid = string.Empty;
        string callsign = string.Empty;
        string airlineIcao = string.Empty;
        string flightNumber = string.Empty;
        string route = string.Empty;
        string aircraftIcao = string.Empty;
        int cruiseFl = 0;
        int initialAltitude = 0;

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

                originName = TryGetString(originElem, "name")
                    ?? TryGetString(originElem, "airport_name")
                    ?? originName;

                // Planned departure runway (several possible field names)
                plannedDepRunway = TryGetString(originElem, "plan_rwy")
                    ?? TryGetString(originElem, "plan_runway")
                    ?? TryGetString(originElem, "dep_rwy")
                    ?? TryGetString(originElem, "orig_rwy")
                    ?? plannedDepRunway;

                // Planned SID identifier (try various field names)
                plannedSid = TryGetString(originElem, "sid_id")
                    ?? TryGetString(originElem, "sid")
                    ?? TryGetString(originElem, "orig_sid")
                    ?? TryGetString(originElem, "plan_sid")
                    ?? plannedSid;

                originMetar = TryGetString(originElem, "metar")
                    ?? TryGetString(originElem, "wx_metar")
                    ?? TryGetString(originElem, "wx_obs")
                    ?? originMetar;
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

                destName = TryGetString(destElem, "name")
                    ?? TryGetString(destElem, "airport_name")
                    ?? destName;

                plannedArrRunway = TryGetString(destElem, "plan_rwy")
                    ?? TryGetString(destElem, "plan_runway")
                    ?? TryGetString(destElem, "arr_rwy")
                    ?? plannedArrRunway;

                destMetar = TryGetString(destElem, "metar")
                    ?? TryGetString(destElem, "wx_metar")
                    ?? TryGetString(destElem, "wx_obs")
                    ?? destMetar;
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
            if (gen.TryGetProperty("icao_airline", out var ai) && ai.ValueKind == JsonValueKind.String)
                airlineIcao = ai.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(airlineIcao) && gen.TryGetProperty("airline_icao", out var aiIcao) && aiIcao.ValueKind == JsonValueKind.String)
                airlineIcao = aiIcao.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(airlineIcao) && gen.TryGetProperty("airline", out var ai2) && ai2.ValueKind == JsonValueKind.String)
                airlineIcao = ai2.GetString() ?? string.Empty;

            if (gen.TryGetProperty("callsign", out var cs) && cs.ValueKind == JsonValueKind.String)
                callsign = cs.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(callsign) && gen.TryGetProperty("atc_callsign", out var atcCs) && atcCs.ValueKind == JsonValueKind.String)
                callsign = atcCs.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(callsign) && gen.TryGetProperty("callsig", out var csShort) && csShort.ValueKind == JsonValueKind.String)
                callsign = csShort.GetString() ?? string.Empty;

            if (gen.TryGetProperty("flight_number", out var fn) && fn.ValueKind == JsonValueKind.String)
                flightNumber = fn.GetString() ?? string.Empty;

            plannedDepRunway = TryGetString(gen, "plan_rwy") 
                ?? TryGetString(gen, "orig_rwy") 
                ?? plannedDepRunway;
            plannedArrRunway = TryGetString(gen, "arr_rwy") 
                ?? TryGetString(gen, "dest_rwy") 
                ?? plannedArrRunway;
            plannedSid = TryGetString(gen, "sid_id") 
                ?? TryGetString(gen, "sid") 
                ?? TryGetString(gen, "orig_sid")
                ?? TryGetString(gen, "plan_sid")
                ?? plannedSid;

            if (string.IsNullOrWhiteSpace(originName))
            {
                originName = TryGetString(gen, "orig_name")
                    ?? TryGetString(gen, "origin_name")
                    ?? originName;
            }
            if (string.IsNullOrWhiteSpace(destName))
            {
                destName = TryGetString(gen, "dest_name")
                    ?? TryGetString(gen, "destination_name")
                    ?? destName;
            }

            if (gen.TryGetProperty("route", out var r) && r.ValueKind == JsonValueKind.String)
                route = r.GetString() ?? string.Empty;

            // Aircraft ICAO/type (common SimBrief fields)
            aircraftIcao = TryGetString(gen, "aircraft_icao")
                ?? TryGetString(gen, "aircraft_type")
                ?? TryGetString(gen, "aircraft")
                ?? TryGetString(gen, "airframe_icao")
                ?? TryGetString(gen, "aircraft_short")
                ?? TryGetString(gen, "type")
                ?? aircraftIcao;

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
                    initialAltitude = feet;
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

            if (root.TryGetProperty("aircraft_icao", out var aIcaoFlat) && aIcaoFlat.ValueKind == JsonValueKind.String)
                aircraftIcao = aIcaoFlat.GetString() ?? aircraftIcao;
        }

        // Aircraft object (v2 structure)
        if (string.IsNullOrWhiteSpace(aircraftIcao) && root.TryGetProperty("aircraft", out var aircraftObj) && aircraftObj.ValueKind == JsonValueKind.Object)
        {
            aircraftIcao = TryGetString(aircraftObj, "icao_code")
                ?? TryGetString(aircraftObj, "icao")
                ?? TryGetString(aircraftObj, "type")
                ?? aircraftIcao;
        }

        // Airframe object (sometimes used instead of "aircraft")
        if (string.IsNullOrWhiteSpace(aircraftIcao) && root.TryGetProperty("airframe", out var airframeObj) && airframeObj.ValueKind == JsonValueKind.Object)
        {
            aircraftIcao = TryGetString(airframeObj, "icao_code")
                ?? TryGetString(airframeObj, "icao")
                ?? TryGetString(airframeObj, "type")
                ?? aircraftIcao;
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

        // Try to extract SID from route string if not found elsewhere
        // Route format: EGPH/24 N0430F250 GOSA1C GOSAM DCT ... LAKE1M EGCC/23R
        if (string.IsNullOrWhiteSpace(plannedSid) && !string.IsNullOrWhiteSpace(route))
        {
            var routeTokens = route.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in routeTokens)
            {
                // Skip origin/dest airport tokens (contain '/')
                if (token.Contains('/')) continue;
                // Skip speed/altitude tokens (start with N or M followed by digits)
                if (Regex.IsMatch(token, @"^[NM]\d{4}[FA]\d{3}$", RegexOptions.IgnoreCase)) continue;
                // Skip DCT, airways (letter + numbers like L612, UL612)
                if (token == "DCT") continue;
                if (Regex.IsMatch(token, @"^[A-Z]{1,2}\d{1,3}$", RegexOptions.IgnoreCase)) continue;
                
                // First remaining token that looks like a SID (letters + digit + letter/digit pattern)
                if (Regex.IsMatch(token, @"^[A-Z]{2,5}\d[A-Z0-9]?$", RegexOptions.IgnoreCase))
                {
                    plannedSid = token.ToUpperInvariant();
                    Console.WriteLine($"[SimBrief] Extracted SID from route: {plannedSid}");
                    break;
                }
            }
        }

        // Debug: print what SID was found
        if (!string.IsNullOrWhiteSpace(plannedSid))
        {
            Console.WriteLine($"[SimBrief] Parsed SID: {plannedSid}");
        }

        if (string.IsNullOrWhiteSpace(originIcao) || string.IsNullOrWhiteSpace(destIcao))
        {
            return null;
        }

        // Reconstruct callsign if missing but airline + flight number exist
        if (string.IsNullOrWhiteSpace(callsign) && !string.IsNullOrWhiteSpace(airlineIcao) && !string.IsNullOrWhiteSpace(flightNumber))
        {
            callsign = $"{airlineIcao}{flightNumber}";
        }
        else if (!string.IsNullOrWhiteSpace(callsign) && string.IsNullOrWhiteSpace(airlineIcao))
        {
            var match = Regex.Match(callsign, @"^(?<icao>[A-Z]{3})(?<num>\d+)$");
            if (match.Success)
            {
                airlineIcao = match.Groups["icao"].Value;
                if (string.IsNullOrWhiteSpace(flightNumber))
                {
                    flightNumber = match.Groups["num"].Value;
                }
            }
        }

        originName = originName?.Trim() ?? string.Empty;
        destName = destName?.Trim() ?? string.Empty;

        return new FlightPlan
        {
            OriginIcao = originIcao.ToUpperInvariant(),
            OriginMetar = string.IsNullOrWhiteSpace(originMetar) ? null : originMetar.Trim(),
            DestinationIcao = destIcao.ToUpperInvariant(),
            DestinationMetar = string.IsNullOrWhiteSpace(destMetar) ? null : destMetar.Trim(),
            AlternateIcao = altIcao.ToUpperInvariant(),
            AirlineIcao = airlineIcao.ToUpperInvariant(),
            Callsign = callsign.ToUpperInvariant(),
            FlightNumber = flightNumber,
            PlannedDepartureRunway = plannedDepRunway.ToUpperInvariant(),
            PlannedArrivalRunway = plannedArrRunway.ToUpperInvariant(),
            PlannedSid = plannedSid.ToUpperInvariant(),
            Route = route,
            CruiseFlightLevel = cruiseFl,
            InitialAltitude = initialAltitude,
            AircraftIcao = aircraftIcao.ToUpperInvariant(),
            WaypointIdentifiers = waypoints,
            OriginName = originName,
            DestinationName = destName
        };
    }

    private FlightPlan? ParseXmlResponse(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);
        var root = doc.Root;
        if (root == null)
            return null;

        var originElem = root.Element("origin");
        var destElem = root.Element("destination");
        var originIcao = originElem?.Element("icao_code")?.Value ?? originElem?.Value ?? string.Empty;
        var originName = originElem?.Element("name")?.Value ?? originElem?.Element("orig_name")?.Value ?? string.Empty;
        var originMetar = originElem?.Element("metar")?.Value ?? originElem?.Element("wx_obs")?.Value ?? string.Empty;
        var destIcao = destElem?.Element("icao_code")?.Value ?? destElem?.Value ?? string.Empty;
        var destName = destElem?.Element("name")?.Value ?? destElem?.Element("dest_name")?.Value ?? string.Empty;
        var destMetar = destElem?.Element("metar")?.Value ?? destElem?.Element("wx_obs")?.Value ?? string.Empty;
        var altIcao = root.Element("alternate")?.Element("icao_code")?.Value ?? 
                     root.Element("alternate")?.Value ?? string.Empty;

        var plannedDepRunway = originElem?.Element("plan_rwy")?.Value
            ?? originElem?.Element("orig_rwy")?.Value
            ?? string.Empty;
        var plannedArrRunway = destElem?.Element("plan_rwy")?.Value
            ?? destElem?.Element("arr_rwy")?.Value
            ?? string.Empty;
        var plannedSid = originElem?.Element("sid")?.Value
            ?? root.Element("general")?.Element("sid")?.Value
            ?? string.Empty;

        var general = root.Element("general");
        var airlineIcao = general?.Element("icao_airline")?.Value
            ?? general?.Element("airline_icao")?.Value
            ?? general?.Element("airline")?.Value
            ?? root.Element("airline_icao")?.Value
            ?? root.Element("airline")?.Value
            ?? string.Empty;
        var callsign = general?.Element("callsign")?.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(callsign))
        {
            callsign = general?.Element("atc_callsign")?.Value ?? general?.Element("callsig")?.Value ?? string.Empty;
        }
        var flightNumber = general?.Element("flight_number")?.Value ?? string.Empty;
        var route = general?.Element("route")?.Value ?? string.Empty;
        var aircraftIcao = general?.Element("aircraft_icao")?.Value
            ?? general?.Element("aircraft_type")?.Value
            ?? general?.Element("aircraft")?.Value
            ?? general?.Element("airframe_icao")?.Value
            ?? general?.Element("aircraft_short")?.Value
            ?? general?.Element("type")?.Value
            ?? root.Element("aircraft")?.Element("icao_code")?.Value
            ?? root.Element("aircraft")?.Element("icao")?.Value
            ?? root.Element("aircraft")?.Element("type")?.Value
            ?? root.Element("airframe")?.Element("icao_code")?.Value
            ?? root.Element("airframe")?.Element("icao")?.Value
            ?? root.Element("airframe")?.Element("type")?.Value
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(plannedDepRunway))
        {
            plannedDepRunway = general?.Element("plan_rwy")?.Value
                ?? general?.Element("orig_rwy")?.Value
                ?? plannedDepRunway;
        }
        if (string.IsNullOrWhiteSpace(plannedArrRunway))
        {
            plannedArrRunway = general?.Element("arr_rwy")?.Value
                ?? general?.Element("dest_rwy")?.Value
                ?? plannedArrRunway;
        }
        if (string.IsNullOrWhiteSpace(plannedSid))
        {
            plannedSid = general?.Element("sid")?.Value
                ?? plannedSid;
        }
        if (string.IsNullOrWhiteSpace(originName))
        {
            originName = general?.Element("orig_name")?.Value
                ?? general?.Element("origin_name")?.Value
                ?? originName;
        }
        if (string.IsNullOrWhiteSpace(destName))
        {
            destName = general?.Element("dest_name")?.Value
                ?? general?.Element("destination_name")?.Value
                ?? destName;
        }
        
        int cruiseFl = 0;
        int initialAltitude = 0;
        var initialAlt = general?.Element("initial_altitude")?.Value;
        if (int.TryParse(initialAlt, out var feet) && feet > 0)
        {
            initialAltitude = feet;
            cruiseFl = feet / 100;
        }

        if (string.IsNullOrWhiteSpace(originIcao) || string.IsNullOrWhiteSpace(destIcao))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(callsign) && !string.IsNullOrWhiteSpace(airlineIcao) && !string.IsNullOrWhiteSpace(flightNumber))
        {
            callsign = $"{airlineIcao}{flightNumber}";
        }
        else if (!string.IsNullOrWhiteSpace(callsign) && string.IsNullOrWhiteSpace(airlineIcao))
        {
            var match = Regex.Match(callsign, @"^(?<icao>[A-Z]{3})(?<num>\d+)$");
            if (match.Success)
            {
                airlineIcao = match.Groups["icao"].Value;
                if (string.IsNullOrWhiteSpace(flightNumber))
                {
                    flightNumber = match.Groups["num"].Value;
                }
            }
        }

        originName = originName?.Trim() ?? string.Empty;
        destName = destName?.Trim() ?? string.Empty;

        return new FlightPlan
        {
            OriginIcao = originIcao.ToUpperInvariant(),
            OriginMetar = string.IsNullOrWhiteSpace(originMetar) ? null : originMetar.Trim(),
            DestinationIcao = destIcao.ToUpperInvariant(),
            DestinationMetar = string.IsNullOrWhiteSpace(destMetar) ? null : destMetar.Trim(),
            AlternateIcao = altIcao.ToUpperInvariant(),
            AirlineIcao = airlineIcao.ToUpperInvariant(),
            Callsign = callsign.ToUpperInvariant(),
            FlightNumber = flightNumber,
            PlannedDepartureRunway = plannedDepRunway.ToUpperInvariant(),
            PlannedArrivalRunway = plannedArrRunway.ToUpperInvariant(),
            PlannedSid = plannedSid.ToUpperInvariant(),
            Route = route,
            CruiseFlightLevel = cruiseFl,
            AircraftIcao = aircraftIcao.ToUpperInvariant(),
            OriginName = originName,
            DestinationName = destName
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}