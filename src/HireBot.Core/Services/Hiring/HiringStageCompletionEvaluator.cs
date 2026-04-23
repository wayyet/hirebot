using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Hiring.Discovery;

namespace HireBot.Core.Services.Hiring;

internal sealed class HiringStageCompletionEvaluator
{
    public IReadOnlyList<HiringStageCompletionDto> Evaluate(
        IReadOnlyList<DiscoveryStageRule> stageRules,
        IReadOnlyDictionary<string, string?> structuredData)
    {
        var completions = new List<HiringStageCompletionDto>(stageRules.Count);
        foreach (var rule in stageRules)
        {
            var satisfiedFields = new List<string>();
            var blockingFields = new List<string>();

            foreach (var field in rule.RequiredFields)
            {
                if (structuredData.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    satisfiedFields.Add(field);
                }
                else
                {
                    blockingFields.Add(field);
                }
            }

            var requiredFieldCount = rule.RequiredFields.Count;
            var satisfiedFieldCount = satisfiedFields.Count;
            var completionRate = requiredFieldCount == 0
                ? 1m
                : Math.Round((decimal)satisfiedFieldCount / requiredFieldCount, 2, MidpointRounding.AwayFromZero);

            completions.Add(new HiringStageCompletionDto(
                Stage: rule.Stage,
                RequiredFieldCount: requiredFieldCount,
                SatisfiedFieldCount: satisfiedFieldCount,
                CompletionRate: completionRate,
                SatisfiedFields: satisfiedFields,
                BlockingFields: blockingFields,
                ReadyForNextStage: blockingFields.Count == 0));
        }

        return completions;
    }
}
