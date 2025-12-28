using System;
using Xunit;
using AeroAI.Atc;
using AeroAI.Data;

namespace AeroAI.Tests;

public class ResolvedContextBuilderTests
{
	[Fact]
	public void Build_WithSimBriefData_ResolvesCallsignAndAirports()
	{
		// Arrange
		var context = new FlightContext
		{
			RawCallsign = "ACA223",
			AirlineIcao = "ACA",
			FlightNumber = "223",
			AirlineName = "Air Canada",
			RadioCallsign = "Air Canada 223",
			OriginIcao = "CYVR",
			OriginName = "Vancouver International Airport",
			DestinationIcao = "CYYC",
			DestinationName = "Calgary International Airport"
		};

		// Act
		var resolved = ResolvedContextBuilder.Build(context);

		// Assert
		Assert.Equal("ACA223", resolved.CallsignRaw);
		Assert.Equal("Air Canada two two three", resolved.CallsignSpoken);
		Assert.Equal("CYVR", resolved.DepartureIcao);
		Assert.Equal("CYYC", resolved.ArrivalIcao);
		Assert.Equal("Vancouver", resolved.DepartureSpoken); // Should extract city from full name
		Assert.Equal("Calgary", resolved.ArrivalSpoken); // Should extract city from full name
		Assert.Equal("simbrief", resolved.DepartureSource);
		Assert.Equal("simbrief", resolved.ArrivalSource);
	}

	[Fact]
	public void Build_WithoutSimBriefName_FallsBackToAirportsJson()
	{
		// Arrange
		var context = new FlightContext
		{
			RawCallsign = "BAW456",
			AirlineIcao = "BAW",
			FlightNumber = "456",
			AirlineName = "British Airways",
			RadioCallsign = "Speedbird 456",
			OriginIcao = "EGLL",
			OriginName = string.Empty, // SimBrief didn't provide name
			DestinationIcao = "EGCC",
			DestinationName = string.Empty // SimBrief didn't provide name
		};

		// Act
		var resolved = ResolvedContextBuilder.Build(context);

		// Assert
		Assert.Equal("BAW456", resolved.CallsignRaw);
		Assert.Equal("Speedbird four five six", resolved.CallsignSpoken);
		Assert.Equal("EGLL", resolved.DepartureIcao);
		Assert.Equal("EGCC", resolved.ArrivalIcao);
		// Should fall back to airports.json (if available)
		Assert.NotNull(resolved.DepartureSpoken);
		Assert.NotNull(resolved.ArrivalSpoken);
		// Source should be airports.json if SimBrief name was empty
		if (string.IsNullOrWhiteSpace(context.OriginName))
		{
			Assert.True(resolved.DepartureSource == "airports.json" || resolved.DepartureSource == "icao_fallback");
		}
	}

	[Fact]
	public void Build_ConvertsFlightNumbersToSpokenDigits()
	{
		// Arrange
		var context = new FlightContext
		{
			RawCallsign = "EZY123",
			AirlineIcao = "EZY",
			FlightNumber = "123",
			AirlineName = "EasyJet",
			RadioCallsign = "EasyJet 123"
		};

		// Act
		var resolved = ResolvedContextBuilder.Build(context);

		// Assert
		Assert.Equal("EasyJet one two three", resolved.CallsignSpoken);
	}

	[Fact]
	public void Build_WithMissingCallsign_ReturnsNull()
	{
		// Arrange
		var context = new FlightContext
		{
			RawCallsign = string.Empty,
			AirlineIcao = string.Empty,
			FlightNumber = string.Empty
		};

		// Act
		var resolved = ResolvedContextBuilder.Build(context);

		// Assert
		Assert.Null(resolved.CallsignRaw);
		Assert.Null(resolved.CallsignSpoken);
		Assert.False(resolved.HasCallsign);
	}
}

