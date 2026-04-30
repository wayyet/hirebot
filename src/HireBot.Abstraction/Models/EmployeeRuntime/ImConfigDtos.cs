namespace HireBot.Abstraction.Models.EmployeeRuntime;

public sealed record ImWebhookUrlDto(
    string Platform,
    string WebhookUrl);

public sealed record ImConfigRequestDto(
    string ConnectionMode,
    string? AppId,
    string? AppSecret,
    string? EncryptKey,
    string? Token,
    string? AesKey,
    string? VerificationToken,
    string? CorpId,
    string? AgentId,
    string? AgentSecret);

public sealed record ImConfigResultDto(
    string Platform,
    string ConnectionMode,
    string Status,
    string Message,
    DateTimeOffset? ConfiguredAt);

public sealed record ImConfigItemDto(
    string Platform,
    string Status,
    string? ConnectionMode,
    string? WebhookPath,
    DateTimeOffset? ConfiguredAt,
    string? LastError);

public sealed record ImConfigStatusDto(
    IReadOnlyList<ImConfigItemDto> Configs);

public sealed record ImWebhookHandleResultDto(
    string Status,
    string? Reply);

