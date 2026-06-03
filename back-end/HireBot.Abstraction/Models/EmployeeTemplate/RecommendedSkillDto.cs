using System.Text.Json.Serialization;

namespace HireBot.Abstraction.Models.EmployeeTemplate;

public sealed record RecommendedSkillDto(
    [property: JsonPropertyName("skill_id")] string SkillId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("current_version")] string CurrentVersion,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("score")] decimal Score,
    [property: JsonPropertyName("matched_keywords")] IReadOnlyList<string> MatchedKeywords,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("can_download")] bool CanDownload);
