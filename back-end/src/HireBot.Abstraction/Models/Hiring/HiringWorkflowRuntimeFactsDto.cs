namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringWorkflowRuntimeFactsDto
{
    public bool MaterialReady { get; init; }

    public IReadOnlyList<string> MaterialClassifiedFiles { get; init; } = [];

    public IReadOnlyDictionary<string, string> MaterialExtractionTargets { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool SkillBaselineReviewed { get; init; }

    public bool SkillBaselineConfirmed { get; init; }

    public static HiringWorkflowRuntimeFactsDto Empty { get; } = new();
}
