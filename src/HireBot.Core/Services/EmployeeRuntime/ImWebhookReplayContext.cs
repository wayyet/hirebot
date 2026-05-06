using HireBot.Abstraction.Services.EmployeeRuntime;

namespace HireBot.Core.Services.EmployeeRuntime;

/// <summary>
/// IM Webhook 重放上下文，用于控制消息重放行为。
/// </summary>
public sealed class ImWebhookReplayContext : IImWebhookReplayContext
{
    /// <summary>
    /// 是否跳过外发消息发送。
    /// </summary>
    public bool SkipOutboundSend { get; set; }

    /// <summary>
    /// 是否使用模拟的 KingCrew 回复。
    /// </summary>
    public bool UseMockKingCrew { get; set; }

    /// <summary>
    /// 模拟的 KingCrew 回复内容。
    /// </summary>
    public string? MockKingCrewReply { get; set; }

    /// <summary>
    /// 重置上下文状态。
    /// </summary>
    public void Reset()
    {
        SkipOutboundSend = false;
        UseMockKingCrew = false;
        MockKingCrewReply = null;
    }
}