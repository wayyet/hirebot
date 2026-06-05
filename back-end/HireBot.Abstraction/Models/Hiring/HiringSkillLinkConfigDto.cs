namespace HireBot.Abstraction.Models.Hiring;

public static class HiringSkillLinkSubmissionModes
{
    public const string Pending = "pending";
    public const string Configured = "configured";
}

public sealed record HiringSkillLinkConfigDto
{
    public string SubmissionMode { get; init; } = HiringSkillLinkSubmissionModes.Pending;

    public IReadOnlyList<HiringLinkedSkillItemDto> LinkedSkills { get; init; } = [];

    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

public sealed record HiringLinkedSkillItemDto
{
    public string SkillId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string VersionId { get; init; } = string.Empty;

    public string CurrentVersion { get; init; } = string.Empty;

    public string BindingMode { get; init; } = "manual";
}
