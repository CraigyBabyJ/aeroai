using AeroAI.Atc;
using Xunit;

namespace AeroAI.Tests;

public class SpokenNumberNormalizerTests
{
	[Theory]
	[InlineData("ryanair one one three", "ryanair 113")]
	[InlineData("easyjet one four one three", "easyjet 1413")]
	[InlineData("speedbird two five", "speedbird 25")]
	[InlineData("lufthansa three seven niner", "lufthansa 379")]
	public void NormalizesCallsignNumbers(string input, string expected)
	{
		var result = SpokenNumberNormalizer.Normalize(input);
		Assert.Equal(expected, result);
	}

	[Theory]
	[InlineData("stand fifteen", "stand 15")]
	[InlineData("on stand fifteen", "on stand 15")]
	[InlineData("gate bravo twelve", "gate bravo 12")]
	[InlineData("stand one five", "stand 15")]
	public void NormalizesStandGateNumbers(string input, string expected)
	{
		var result = SpokenNumberNormalizer.Normalize(input);
		Assert.Equal(expected, result);
	}

	[Theory]
	[InlineData("squawk five two four zero", "squawk 5240")]
	[InlineData("squawk seven seven zero zero", "squawk 7700")]
	[InlineData("squawk one two zero zero", "squawk 1200")]
	public void NormalizesSquawkNumbers(string input, string expected)
	{
		var result = SpokenNumberNormalizer.Normalize(input);
		Assert.Equal(expected, result);
	}

	[Theory]
	[InlineData("flight level three three zero", "flight level 330")]
	[InlineData("FL three five zero", "FL 350")]
	[InlineData("climb three three zero", "climb 330")]
	public void NormalizesFlightLevelNumbers(string input, string expected)
	{
		var result = SpokenNumberNormalizer.Normalize(input);
		Assert.Equal(expected, result);
	}

	[Theory]
	[InlineData("one one three", "113")]
	[InlineData("one four one three", "1413")]
	[InlineData("three seven niner", "379")]
	public void ConvertSpokenSequenceToDigits(string input, string expected)
	{
		var result = SpokenNumberNormalizer.ConvertSpokenSequenceToDigits(input);
		Assert.Equal(expected, result);
	}
}

