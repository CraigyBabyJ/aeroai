namespace AeroAI.Atc;

public enum AtcState
{
	Idle,
	IfrRequested,
	ClearancePendingData,
	ClearanceCollectingTrainingData,
	ClearanceReady,
	ClearanceIssued
}
