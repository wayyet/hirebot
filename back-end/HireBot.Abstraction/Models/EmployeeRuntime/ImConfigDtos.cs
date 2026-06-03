namespace HireBot.Abstraction.Models.EmployeeRuntime;

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
    string? AgentSecret,
    string? BotId,
    string? BotSecret);

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

/// <summary>
/// 钉钉IM配置实体
/// </summary>
public sealed class DingTalkChannelConfig
{
    public bool Enabled { get; set; } = false;
    public string? AppId { get; set; }
    public string? AppKey { get; set; }
   
    public string? AppSecret { get; set; }
    public string AppSecretRef { get; set; } = "env:DINGTALK_APP_SECRET";
 
}



/// <summary>
/// 飞书频道配置类。
/// 用于更新飞书频道的配置参数。
/// </summary>
public sealed class FeishuChannelConfig
{
    /// <summary>
    /// 是否启用飞书频道。
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 飞书 App ID。
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// App ID 的引用（如 "env:FEISHU_APP_ID"）。
    /// </summary>
    public string AppIdRef { get; set; } = "env:FEISHU_APP_ID";

    /// <summary>
    /// 飞书 App Secret。
    /// </summary>
    public string? AppSecret { get; set; }

    /// <summary>
    /// App Secret 的引用（如 "env:FEISHU_APP_SECRET"）。
    /// </summary>
    public string AppSecretRef { get; set; } = "env:FEISHU_APP_SECRET";

    /// <summary>
    /// 群聊策略："open" 允许所有群组，"allowlist" 限制为允许的群组ID，"disabled" 丢弃群消息。
    /// </summary>
    public string GroupPolicy { get; set; } = "open";

    /// <summary>
    /// 允许的发送者 open_id 列表。为空则允许所有发送者。
    /// </summary>
    public string[] AllowedFromUserIds { get; set; } = [];
}



/// <summary>
/// 企业微信频道配置类（发送给 KingCrab 网关）。
/// </summary>
public sealed class WeComChannelConfig
{
    public bool Enabled { get; set; } = false;
    public string? BotId { get; set; }
    public string BotIdRef { get; set; } = "env:WECOM_BOT_ID";
    public string? BotSecret { get; set; }
    public string BotSecretRef { get; set; } = "env:WECOM_BOT_SECRET";
}

/// <summary>
/// 企业微信频道当前生效配置（从 KingCrab 网关获取）。
/// </summary>
public sealed record WeComChannelEffectiveConfigDto(
    bool Enabled,
    string? BotId,
    string BotIdRef,
    string? BotSecret,
    string BotSecretRef);


