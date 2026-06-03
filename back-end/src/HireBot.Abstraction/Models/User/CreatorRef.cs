namespace HireBot.Abstraction.Models.User;

/// <summary>
/// 创建人/更新人信息引用 DTO
/// </summary>
public sealed record CreatorRef
{
    public string? Username { get; init; }
    public string? DisplayName { get; init; }
    public string? FamilyName { get; init; }
    public string? GivenName { get; init; }
}
