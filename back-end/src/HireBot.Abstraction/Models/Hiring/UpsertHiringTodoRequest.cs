using System.Text.Json;

namespace HireBot.Abstraction.Models.Hiring;

/// <summary>新建或更新 TODO（Handoff）事项的请求参数。</summary>
public sealed record UpsertHiringTodoRequest(
    string HandoffId,
    string Title,
    string Kind,
    string Stage,
    string TargetSkill,
    string Status,
    string? Intent = null,
    string? Category = null,
    string? Source = null,
    string? Acceptance = null,
    JsonElement? Payload = null,
    IReadOnlyList<string>? RelatedHandoffIds = null,
    IReadOnlyList<string>? RelatedFiles = null);
