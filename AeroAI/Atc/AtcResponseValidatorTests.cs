using System;
using AeroAI.Models;
using AeroAI.Data;
using Xunit;

namespace AeroAI.Atc;

public class AtcResponseValidatorTests
{
	private static (AtcContext Ctx, FlightContext Flight) CreateContexts()
	{
		var ctx = new AtcContext
		{
			ClearanceDecision = new ClearanceDecision
			{
				ClearanceType = "IFR_CLEARANCE",
				ClearedTo = "EGSS",
				DepRunway = "24",
				Sid = "GOSAC1C",
				InitialAltitudeFt = 5000,
				Squawk = "1416"
			},
			FlightInfo = new FlightInfo
			{
				DepIcao = "EGCC",
				ArrIcao = "EGSS",
				ArrName = "London Stansted",
				CruiseLevel = "FL350"
			}
		};

		var flight = new FlightContext
		{
			OriginIcao = "EGCC",
			DestinationIcao = "EGSS",
			DestinationName = "London Stansted",
			CruiseFlightLevel = 350,
			ClearedAltitude = 5000,
			SquawkCode = "1416",
			DepartureRunway = new NavRunwaySummary
			{
				AirportIcao = "EGCC",
				RunwayIdentifier = "24"
			}
		};

		return (ctx, flight);
	}

	[Fact]
	public void Accepts_Normalized_Readback_When_Matching_Context()
	{
		var (ctx, flight) = CreateContexts();
		var llmResponse = "Runway two four, initial climb 5000, expect flight level 350, squawk one four one six.";

		var result = AtcResponseValidator.Validate(llmResponse, ctx, flight);

		Assert.True(result.IsValid);
		Assert.Empty(result.Reasons);
	}

	[Fact]
	public void Rejects_Mismatched_Runway()
	{
		var (ctx, flight) = CreateContexts();
		var llmResponse = "Taxi to runway two three, squawk one four one six.";

		var result = AtcResponseValidator.Validate(llmResponse, ctx, flight);

		Assert.False(result.IsValid);
		Assert.Contains(result.Reasons, r => r.Contains("Runway", StringComparison.OrdinalIgnoreCase));
		Assert.Contains("23", result.OffendingTokens);
	}

	[Fact]
	public void Rejects_Mismatched_Squawk_And_Altitude()
	{
		var (ctx, flight) = CreateContexts();
		var llmResponse = "Cleared as filed, initial climb 7000 feet, squawk one two three four.";

		var result = AtcResponseValidator.Validate(llmResponse, ctx, flight);

		Assert.False(result.IsValid);
		Assert.Contains(result.Reasons, r => r.Contains("Altitude", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(result.Reasons, r => r.Contains("Squawk", StringComparison.OrdinalIgnoreCase));
		Assert.Contains("7000", result.OffendingTokens);
		Assert.Contains("1234", result.OffendingTokens);
	}

	[Fact]
	public void Rejects_When_Spoken_Icao_Token()
	{
		var (ctx, flight) = CreateContexts();
		var llmResponse = "Cleared to EGSS, runway two four.";

		var result = AtcResponseValidator.Validate(llmResponse, ctx, flight);

		Assert.False(result.IsValid);
		Assert.Contains(result.Reasons, r => r.Contains("ICAO", StringComparison.OrdinalIgnoreCase));
		Assert.Contains("EGSS", result.OffendingTokens);
	}

	[Fact]
	public void Accepts_When_Using_Airport_Name_Not_Icao()
	{
		var (ctx, flight) = CreateContexts();
		var llmResponse = "Cleared to London Stansted, runway two four.";

		var result = AtcResponseValidator.Validate(llmResponse, ctx, flight);

		Assert.True(result.IsValid);
		Assert.Empty(result.Reasons);
	}

	[Fact]
	public void Ignores_Random_Four_Letter_Word_Not_Known_Icao()
	{
		Assert.False(AeroAI.Data.AirportNameResolver.IsKnownAirportIcao("QWER"));

		var (ctx, flight) = CreateContexts();
		var llmResponse = "Proceed direct QWER, maintain present heading.";

		var result = AtcResponseValidator.Validate(llmResponse, ctx, flight);

		Assert.True(result.IsValid);
	}

	[Fact]
	public void ClearanceDecision_Uses_Airport_Name_Not_Icao()
	{
		var flight = new FlightContext
		{
			Callsign = "TEST123",
			OriginIcao = "EGLL",
			OriginName = "London Heathrow",
			DestinationIcao = "EGCC",
			DestinationName = "Manchester",
			CruiseFlightLevel = 350,
			SquawkCode = "1234",
			ClearedAltitude = 5000
		};

		var ctx = FlightContextToAtcContextMapper.Map(flightContext: flight, ifrClearanceIssued: false, pilotIntent: new PilotIntent { Type = IntentType.RequestClearance });

		Assert.Equal("Manchester", ctx.ClearanceDecision.ClearedTo);
	}
}
