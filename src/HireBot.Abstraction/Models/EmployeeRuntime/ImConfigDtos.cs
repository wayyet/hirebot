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

public sealed record FeishuChannelEffectiveConfigDto(
    bool Enabled,
    string? AppId,
    string AppIdRef,
    string? AppSecret,
    string AppSecretRef,
    string GroupPolicy,
    string[] AllowedFromUserIds,
    string[] AllowedGroupIds,
    int MaxInboundChars,
    bool RequireMentionInGroup,
    bool ExposeInboundMediaUrls);

public sealed record ImConfigItemDto(
    string Platform,
    string Status,
    string? ConnectionMode,
    string? WebhookPath,
    DateTimeOffset? ConfiguredAt,
    string? LastError,
    string? AppId = null,
    string? AppSecret = null,
    string? EncryptKey = null,
    string? Token = null,
    string? AesKey = null,
    string? VerificationToken = null,
    string? CorpId = null,
    string? AgentId = null,
    string? AgentSecret = null);

public sealed record ImConfigStatusDto(
    IReadOnlyList<ImConfigItemDto> Configs);

public sealed record ImWebhookHandleResultDto(
    string Status,
    string? Reply);

