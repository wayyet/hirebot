namespace HireBot.Abstraction.Models.Hiring;

public sealed record HireTemplateResultDto(
    string HireId,
    string SandboxId,
    string Status,
    string NextAction,
    string? SessionId = null,
    // 沙箱处于 Running+Initialized 状态时直接返回，省去前端额外的状态轮询
    string? GatewayEndpoint = null);
