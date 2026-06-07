using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Core.Services.Hiring;

internal sealed record HiringSkillLinkConfigState(
    string SubmissionMode,
    IReadOnlyList<HiringLinkedSkillItemState> LinkedSkills,
    DateTimeOffset UpdatedAtUtc)
{
    public bool HasLinkedSkills => (LinkedSkills?.Count ?? 0) > 0;

    public HiringSkillLinkConfigDto ToDto()
        => new()
        {
            SubmissionMode = ResolveSubmissionMode(this),
            LinkedSkills = (LinkedSkills ?? [])
                .Select(static item => item.ToDto())
                .ToArray(),
            UpdatedAtUtc = UpdatedAtUtc
        };

    public static HiringSkillLinkConfigState FromDto(HiringSkillLinkConfigDto? dto)
    {
        var normalizedLinkedSkills = (dto?.LinkedSkills ?? [])
            .Select(static item => HiringLinkedSkillItemState.FromDto(item))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .GroupBy(static item => item.SkillId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        var provisionalState = new HiringSkillLinkConfigState(
            SubmissionMode: HiringSkillLinkSubmissionModes.Pending,
            LinkedSkills: normalizedLinkedSkills,
            UpdatedAtUtc: dto?.UpdatedAtUtc ?? DateTimeOffset.UtcNow);

        return provisionalState with
        {
            SubmissionMode = NormalizeSubmissionMode(dto?.SubmissionMode, provisionalState)
        };
    }

    private static string NormalizeSubmissionMode(string? submissionMode, HiringSkillLinkConfigState state)
    {
        if (state.HasLinkedSkills)
        {
            return HiringSkillLinkSubmissionModes.Configured;
        }

        return string.Equals(submissionMode, HiringSkillLinkSubmissionModes.Pending, StringComparison.OrdinalIgnoreCase)
            ? HiringSkillLinkSubmissionModes.Pending
            : HiringSkillLinkSubmissionModes.Pending;
    }

    private static string ResolveSubmissionMode(HiringSkillLinkConfigState state)
        => state.HasLinkedSkills
            ? HiringSkillLinkSubmissionModes.Configured
            : HiringSkillLinkSubmissionModes.Pending;
}

internal sealed record HiringLinkedSkillItemState(
    string SkillId,
    string Name,
    string DisplayName,
    string VersionId,
    string CurrentVersion,
    string BindingMode)
{
    public HiringLinkedSkillItemDto ToDto()
        => new()
        {
            SkillId = SkillId,
            Name = Name,
            DisplayName = DisplayName,
            VersionId = VersionId,
            CurrentVersion = CurrentVersion,
            BindingMode = BindingMode
        };

    public static HiringLinkedSkillItemState? FromDto(HiringLinkedSkillItemDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var skillId = dto.SkillId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return null;
        }

        return new HiringLinkedSkillItemState(
            SkillId: skillId,
            Name: dto.Name?.Trim() ?? string.Empty,
            DisplayName: dto.DisplayName?.Trim() ?? string.Empty,
            VersionId: dto.VersionId?.Trim() ?? string.Empty,
            CurrentVersion: dto.CurrentVersion?.Trim() ?? string.Empty,
            BindingMode: string.IsNullOrWhiteSpace(dto.BindingMode) ? "manual" : dto.BindingMode.Trim());
    }
}
