using System;

namespace AeroAI.AtcSession;

public sealed class AtcSessionState
{
    public string SessionId { get; set; } = string.Empty;
    public string CurrentPhase { get; set; } = string.Empty;
    public string CurrentControllerRole { get; set; } = string.Empty;
    public string ActiveControllerRole { get; set; } = string.Empty;
    public string? ActiveFrequencyMhz { get; set; }
    public string FacilityIcao { get; set; } = string.Empty;
    public PendingHandoff? PendingHandoff { get; set; }
    public string? LastAtcAction { get; set; }
    public string? LastResponseText { get; set; }
    public bool ClearanceIssued { get; set; }
    public bool TaxiIssued { get; set; }
    public bool TakeoffCleared { get; set; }
    public bool Airborne { get; set; }
    public string? LastIntentId { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public string[] ExpectedNextIntents { get; set; } = Array.Empty<string>();
}

public sealed class PendingHandoff
{
    public string TargetRole { get; init; } = string.Empty;
    public string? TargetFrequencyMhz { get; init; }
    public string? TargetFacilityIcao { get; init; }
    public DateTime IssuedAtUtc { get; init; } = DateTime.UtcNow;
}
