namespace HireBot.Abstraction.Models.Hiring;

public sealed record HireTemplateResultDto(
    string HireId,
    string SandboxId,
    string Status,
    string NextAction,
    string? SessionId = null,
    // 沙箱处于 Running+Initialized 状态时直接返回，省去前端额外的状态轮询
    string? GatewayEndpoint = null,
    // 新沙箱首次创建时为 true，指示前端通过 WS 发送模板包以驱动 coach 分析引导流程
    bool TemplatePrimingRequired = false);
