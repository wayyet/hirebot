using System.ComponentModel.DataAnnotations;

namespace HireBot.Abstraction.Models.Hiring;

public sealed record HiringConversationMessageRequestDto
{
    [Required]
    public string Content { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string>? StructuredAnswers { get; init; }
}
