using Xunit;

namespace AeroAI.Atc;

public class AircraftTypeResolverLegacyTests
{
    [Fact]
    public void Resolves_Icao_B38M()
    {
        Assert.Equal("B38M", AircraftTypeResolver.ResolveSimple("B38M"));
    }

    [Fact]
    public void Resolves_Model_Name_To_Icao()
    {
        Assert.Equal("B38M", AircraftTypeResolver.ResolveSimple("Boeing 737 MAX 8"));
    }

    [Theory]
    [InlineData("737 max")]
    [InlineData("737 max 8")]
    public void Resolves_Aliases_To_Icao(string input)
    {
        Assert.Equal("B38M", AircraftTypeResolver.ResolveSimple(input));
    }

    [Theory]
    [InlineData("B38M")]
    public void Resolves_Near_Miss_To_Icao(string input)
    {
        Assert.Equal("B38M", AircraftTypeResolver.ResolveSimple(input));
    }
}
