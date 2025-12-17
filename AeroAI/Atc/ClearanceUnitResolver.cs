using AeroAI.Data;

namespace AeroAI.Atc;

/// <summary>
/// Resolves which ATC unit should handle IFR clearance requests using top-down logic,
/// along with the frequency (if available). Falls back to UNICOM (122.800) when no
/// frequencies are published for the airport.
/// </summary>
internal static class ClearanceUnitResolver
{
	internal static ClearanceRoutingResult ResolveForClearance(string? airportIcao, AirportFrequencySet? freqs = null)
	{
		freqs ??= TryGetFrequencies(airportIcao);

		if (freqs == null)
		{
			return ClearanceRoutingResult.Unicom();
		}

		if (freqs.Clearance.HasValue)
			return new ClearanceRoutingResult(AtcUnit.ClearanceDelivery, freqs.Clearance, true);

		if (freqs.Ground.HasValue)
			return new ClearanceRoutingResult(AtcUnit.Ground, freqs.Ground, true);

		if (freqs.Tower.HasValue)
			return new ClearanceRoutingResult(AtcUnit.Tower, freqs.Tower, true);

		if (freqs.Approach.HasValue)
			return new ClearanceRoutingResult(AtcUnit.Approach, freqs.Approach, true);

		if (freqs.Departure.HasValue)
			return new ClearanceRoutingResult(AtcUnit.Center, freqs.Departure, true);

		return ClearanceRoutingResult.Unicom();
	}

	/// <summary>
	/// Resolves a frequency (if any) for the specified ATC unit at an airport.
	/// </summary>
	internal static double? ResolveFrequency(string? airportIcao, AtcUnit unit)
	{
		var freqs = TryGetFrequencies(airportIcao);
		if (freqs == null)
			return null;

		return unit switch
		{
			AtcUnit.ClearanceDelivery => freqs.Clearance,
			AtcUnit.Ground => freqs.Ground,
			AtcUnit.Tower => freqs.Tower,
			AtcUnit.Approach => freqs.Approach,
			AtcUnit.Arrival => freqs.Approach,
			AtcUnit.Center => freqs.Departure,
			AtcUnit.Departure => freqs.Departure,
			_ => null
		};
	}

	private static AirportFrequencySet? TryGetFrequencies(string? airportIcao)
	{
		if (AirportFrequencies.TryGetFrequencies(airportIcao, out var set))
			return set;
		return null;
	}
}

internal readonly record struct ClearanceRoutingResult(AtcUnit Unit, double? FrequencyMhz, bool HasAtc)
{
	public bool IsUnicom => !HasAtc;

	public static ClearanceRoutingResult Unicom() => new ClearanceRoutingResult(AtcUnit.ClearanceDelivery, 122.800, false);
}

