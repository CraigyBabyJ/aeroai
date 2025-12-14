namespace AeroAI.Atc.Vectoring;

public sealed class VectorInstruction
{
	public int? Heading { get; init; }

	public int? Altitude { get; init; }

	public string Phrase { get; init; } = string.Empty;
}
