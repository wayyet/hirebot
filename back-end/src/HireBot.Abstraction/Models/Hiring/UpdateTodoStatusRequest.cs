using System.Text.Json.Serialization;

namespace HireBot.Abstraction.Models.Hiring;

/// <summary>用户更新 TODO 状态的请求参数（确认 / 撤销）。</summary>
public sealed record UpdateTodoStatusRequest(
    [property: JsonPropertyName("status")] string Status);
