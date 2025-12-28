namespace AeroAI.Atc;

/// <summary>
/// Resolved context for ATC communications, ensuring authoritative data from SimBrief
/// is used instead of STT-approximated values. All spoken forms are ready for TTS.
/// </summary>
public sealed class ResolvedContext
{
	/// <summary>
	/// Raw callsign from SimBrief (e.g., "ACA223").
	/// </summary>
	public string? CallsignRaw { get; init; }

	/// <summary>
	/// Spoken callsign for ATC (e.g., "Air Canada two two three").
	/// </summary>
	public string? CallsignSpoken { get; init; }

	/// <summary>
	/// Departure airport ICAO (e.g., "CYVR").
	/// </summary>
	public string? DepartureIcao { get; init; }

	/// <summary>
	/// Arrival airport ICAO (e.g., "CYYC").
	/// </summary>
	public string? ArrivalIcao { get; init; }

	/// <summary>
	/// Spoken departure airport name (prefers city, fallback to full name).
	/// </summary>
	public string? DepartureSpoken { get; init; }

	/// <summary>
	/// Spoken arrival airport name (prefers city, fallback to full name).
	/// </summary>
	public string? ArrivalSpoken { get; init; }

	/// <summary>
	/// Source of departure airport name (SimBrief vs airports.json).
	/// </summary>
	public string DepartureSource { get; init; } = "unknown";

	/// <summary>
	/// Source of arrival airport name (SimBrief vs airports.json).
	/// </summary>
	public string ArrivalSource { get; init; } = "unknown";

	/// <summary>
	/// Returns true if this context has valid callsign data.
	/// </summary>
	public bool HasCallsign => !string.IsNullOrWhiteSpace(CallsignRaw) && !string.IsNullOrWhiteSpace(CallsignSpoken);

	/// <summary>
	/// Returns true if this context has valid departure airport data.
	/// </summary>
	public bool HasDeparture => !string.IsNullOrWhiteSpace(DepartureIcao) && !string.IsNullOrWhiteSpace(DepartureSpoken);

	/// <summary>
	/// Returns true if this context has valid arrival airport data.
	/// </summary>
	public bool HasArrival => !string.IsNullOrWhiteSpace(ArrivalIcao) && !string.IsNullOrWhiteSpace(ArrivalSpoken);
}

