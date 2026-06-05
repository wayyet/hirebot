using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring;

namespace HireBot.Core.Tests;

public sealed class HiringSkillLinkConfigStateTests
{
    [Fact]
    public void FromDto_WhenLinkedSkillsContainDuplicates_ShouldKeepFirstSkillAndForceConfiguredMode()
    {
        var dto = new HiringSkillLinkConfigDto
        {
            SubmissionMode = HiringSkillLinkSubmissionModes.Pending,
            LinkedSkills =
            [
                new HiringLinkedSkillItemDto
                {
                    SkillId = "skill-a",
                    Name = "First",
                    DisplayName = "First Display",
                    VersionId = "v1",
                    CurrentVersion = "1.0.0",
                    BindingMode = "manual"
                },
                new HiringLinkedSkillItemDto
                {
                    SkillId = "skill-a",
                    Name = "Second",
                    DisplayName = "Second Display",
                    VersionId = "v2",
                    CurrentVersion = "2.0.0",
                    BindingMode = "manual"
                }
            ]
        };

        var state = HiringSkillLinkConfigState.FromDto(dto);
        var normalized = state.ToDto();

        Assert.Equal(HiringSkillLinkSubmissionModes.Configured, normalized.SubmissionMode);
        Assert.Single(normalized.LinkedSkills);
        Assert.Equal("skill-a", normalized.LinkedSkills[0].SkillId);
        Assert.Equal("First", normalized.LinkedSkills[0].Name);
        Assert.Equal("v1", normalized.LinkedSkills[0].VersionId);
    }

    [Fact]
    public void FromDto_WhenLinkedSkillsAreEmpty_ShouldNormalizeToPendingMode()
    {
        var dto = new HiringSkillLinkConfigDto
        {
            SubmissionMode = HiringSkillLinkSubmissionModes.Configured,
            LinkedSkills = []
        };

        var state = HiringSkillLinkConfigState.FromDto(dto);
        var normalized = state.ToDto();

        Assert.Equal(HiringSkillLinkSubmissionModes.Pending, normalized.SubmissionMode);
        Assert.Empty(normalized.LinkedSkills);
    }
}
