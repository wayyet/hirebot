using HireBot.Abstraction.Services.EmployeeRuntime;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed class ImWebhookReplayContext : IImWebhookReplayContext
{
    public bool SkipOutboundSend { get; set; }

    public bool UseMockKingCrew { get; set; }

    public string? MockKingCrewReply { get; set; }

    public void Reset()
    {
        SkipOutboundSend = false;
        UseMockKingCrew = false;
        MockKingCrewReply = null;
    }
}
