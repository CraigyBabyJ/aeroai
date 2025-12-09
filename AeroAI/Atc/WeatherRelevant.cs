using System.Text.Json.Serialization;

namespace AeroAI.Atc;

public sealed class WeatherRelevant
{
	[JsonPropertyName("dep_wind_dir")]
	public int? DepWindDir { get; set; }

	[JsonPropertyName("dep_wind_kt")]
	public int? DepWindKt { get; set; }

	[JsonPropertyName("arr_wind_dir")]
	public int? ArrWindDir { get; set; }

	[JsonPropertyName("arr_wind_kt")]
	public int? ArrWindKt { get; set; }

	[JsonPropertyName("qnh_hpa")]
	public int? QnhHpa { get; set; }
}
