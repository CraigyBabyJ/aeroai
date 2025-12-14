namespace AeroAI.Atc;

public enum IntentType
{
	Unknown,
	RequestClearance,
	ReadbackClearance,
	RequestPush,
	ReadyToTaxi,
	ReadyForDeparture,
	AcknowledgeTakeoff,
	ContactDeparture,
	ClimbAcknowledged,
	ContactArrival,
	RunwayInSight,
	AcknowledgeLanding,
	RequestShutdown,
	CheckIn,
	RequestAltitude,
	RequestHeading,
	ReportPosition,
	ReportAltitude,
	ReportHeading
}
