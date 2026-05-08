namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record ImWebhookReplayRequestDto(
    string InstanceId,
    string RawPayload,
    IReadOnlyDictionary<string, string>? Headers,
    bool SkipOutboundSend = true,
    bool UseMockKingCrew = true,
    string? MockKingCrewReply = null);
