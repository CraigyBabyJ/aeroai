using AeroAI.Atc;
using Xunit;

namespace AeroAI.Tests;

public class AircraftTypeResolverTests
{
	[Theory]
	[InlineData("A320", true, "A320", false)]
	[InlineData("B738", true, "B738", false)]
	[InlineData("B77W", true, "B77W", false)]
	public void ResolvesDirectIcaoCodes(string input, bool expectedSuccess, string expectedCode, bool expectedAmbiguous)
	{
		var (success, icaoCode, isAmbiguous) = AircraftTypeResolver.Resolve(input);
		Assert.Equal(expectedSuccess, success);
		Assert.Equal(expectedCode, icaoCode);
		Assert.Equal(expectedAmbiguous, isAmbiguous);
	}

	[Theory]
	[InlineData("Boeing 737", true, "B738", true)]  // Ambiguous, defaults to -800
	[InlineData("Boeing 737 800", true, "B738", false)]
	[InlineData("boeing seven three seven eight hundred", true, "B738", false)]
	[InlineData("737 800", true, "B738", false)]
	[InlineData("737-800", true, "B738", false)]
	public void ResolvesBoeing737Variants(string input, bool expectedSuccess, string expectedCode, bool expectedAmbiguous)
	{
		var (success, icaoCode, isAmbiguous) = AircraftTypeResolver.Resolve(input);
		Assert.Equal(expectedSuccess, success);
		Assert.Equal(expectedCode, icaoCode);
		Assert.Equal(expectedAmbiguous, isAmbiguous);
	}

	[Theory]
	[InlineData("Airbus 320", true, "A320", false)]
	[InlineData("airbus a320", true, "A320", false)]
	[InlineData("a three twenty", true, "A320", false)]
	[InlineData("320 neo", true, "A20N", false)]
	[InlineData("A320 neo", true, "A20N", false)]
	public void ResolvesAirbus320Variants(string input, bool expectedSuccess, string expectedCode, bool expectedAmbiguous)
	{
		var (success, icaoCode, isAmbiguous) = AircraftTypeResolver.Resolve(input);
		Assert.Equal(expectedSuccess, success);
		Assert.Equal(expectedCode, icaoCode);
		Assert.Equal(expectedAmbiguous, isAmbiguous);
	}

	[Theory]
	[InlineData("bowen 737", true, "B738", true)]  // STT typo tolerance
	[InlineData("bowing 737 800", true, "B738", false)]
	public void ToleratesCommonSttTypos(string input, bool expectedSuccess, string expectedCode, bool expectedAmbiguous)
	{
		var (success, icaoCode, isAmbiguous) = AircraftTypeResolver.Resolve(input);
		Assert.Equal(expectedSuccess, success);
		Assert.Equal(expectedCode, icaoCode);
		Assert.Equal(expectedAmbiguous, isAmbiguous);
	}

	[Theory]
	[InlineData("Boeing 737")]
	[InlineData("A320")]
	[InlineData("airbus 320")]
	[InlineData("seven three seven")]
	public void ContainsAircraftType_ReturnsTrue(string input)
	{
		Assert.True(AircraftTypeResolver.ContainsAircraftType(input));
	}

	[Theory]
	[InlineData("requesting clearance")]
	[InlineData("stand fifteen")]
	[InlineData("information alpha")]
	public void ContainsAircraftType_ReturnsFalse(string input)
	{
		Assert.False(AircraftTypeResolver.ContainsAircraftType(input));
	}
}

