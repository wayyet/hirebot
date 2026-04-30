namespace HireBot.Abstraction.Services.EmployeeRuntime;

public interface IImWebhookReplayContext
{
    bool SkipOutboundSend { get; set; }

    bool UseMockKingCrew { get; set; }

    string? MockKingCrewReply { get; set; }

    void Reset();
}
