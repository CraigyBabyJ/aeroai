using System;
using Xunit;
using AeroAI.Atc;

namespace AeroAI.Tests;

public class OutputGuardTests
{
	[Fact]
	public void ScrubOutput_ReplacesDepartureIcao_WithSpokenName()
	{
		// Arrange
		var context = new ResolvedContext
		{
			DepartureIcao = "CYVR",
			DepartureSpoken = "Vancouver"
		};
		var text = "Cleared to CYYC via CYVR departure.";

		// Act
		var result = OutputGuard.ScrubOutput(text, context, null);

		// Assert
		Assert.Contains("Vancouver", result);
		Assert.DoesNotContain("CYVR", result);
	}

	[Fact]
	public void ScrubOutput_ReplacesArrivalIcao_WithSpokenName()
	{
		// Arrange
		var context = new ResolvedContext
		{
			ArrivalIcao = "CYYC",
			ArrivalSpoken = "Calgary"
		};
		var text = "Cleared to CYYC via departure.";

		// Act
		var result = OutputGuard.ScrubOutput(text, context, null);

		// Assert
		Assert.Contains("Calgary", result);
		Assert.DoesNotContain("CYYC", result);
	}

	[Fact]
	public void ScrubOutput_ReplacesRawCallsign_WithSpokenCallsign()
	{
		// Arrange
		var context = new ResolvedContext
		{
			CallsignRaw = "ACA223",
			CallsignSpoken = "Air Canada two two three"
		};
		var text = "ACA223, cleared for takeoff.";

		// Act
		var result = OutputGuard.ScrubOutput(text, context, null);

		// Assert
		Assert.Contains("Air Canada two two three", result);
		Assert.DoesNotContain("ACA223", result);
	}

	[Fact]
	public void ScrubOutput_ReplacesCallsignVariant_WithSpokenCallsign()
	{
		// Arrange
		var context = new ResolvedContext
		{
			CallsignRaw = "ACA223",
			CallsignSpoken = "Air Canada two two three"
		};
		var text = "ACA 223, cleared for takeoff.";

		// Act
		var result = OutputGuard.ScrubOutput(text, context, null);

		// Assert
		Assert.Contains("Air Canada two two three", result);
		Assert.DoesNotContain("ACA 223", result);
	}

	[Fact]
	public void ScrubOutput_WithNullContext_ReturnsOriginal()
	{
		// Arrange
		var text = "Cleared to CYYC.";

		// Act
		var result = OutputGuard.ScrubOutput(text, null, null);

		// Assert
		Assert.Equal(text, result);
	}

	[Fact]
	public void ScrubOutput_ReplacesBothAirports_AndCallsign()
	{
		// Arrange
		var context = new ResolvedContext
		{
			DepartureIcao = "CYVR",
			DepartureSpoken = "Vancouver",
			ArrivalIcao = "CYYC",
			ArrivalSpoken = "Calgary",
			CallsignRaw = "ACA223",
			CallsignSpoken = "Air Canada two two three"
		};
		var text = "ACA223, cleared to CYYC from CYVR.";

		// Act
		var result = OutputGuard.ScrubOutput(text, context, null);

		// Assert
		Assert.Contains("Air Canada two two three", result);
		Assert.Contains("Calgary", result);
		Assert.Contains("Vancouver", result);
		Assert.DoesNotContain("ACA223", result);
		Assert.DoesNotContain("CYYC", result);
		Assert.DoesNotContain("CYVR", result);
	}
}

