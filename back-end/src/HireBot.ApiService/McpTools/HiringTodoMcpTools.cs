using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HireBot.ApiService.McpTools;

/// <summary>
/// MCP 工具：提供雇佣界面的 TODO（Handoff）事项管理能力。
/// userId 和 sessionId 均由 Kingcrab 通过 _meta 传入，无需调用方在参数中显式传递 hireId。
/// </summary>
[McpServerToolType]
internal sealed class HiringTodoMcpTools(IHiringTodoService todoService, ILogger<HiringTodoMcpTools> logger)
{
    [McpServerTool(Name = "hiring.list_todos", ReadOnly = true)]
    [Description("列出当前雇佣会话的所有 TODO 事项（handoff items）。会话上下文由 _meta.sessionId 和 _meta.userId 自动识别，无需传参。")]
    public async Task<string> ListTodosAsync(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var userId = ExtractUserId(requestContext);
        var sessionId = ExtractSessionId(requestContext);
        logger.LogInformation("[MCP] hiring.list_todos 被调用 | userId={UserId} sessionId={SessionId}", userId ?? "<未传入>", sessionId ?? "<未传入>");

        if (userId is null)
            return """{"error":"_meta.userId 未传入，无法验证用户身份"}""";
        if (sessionId is null)
            return """{"error":"_meta.sessionId 未传入，无法定位雇佣会话"}""";

        var response = await todoService.GetTodosAsync(sessionId, userId, cancellationToken);
        return JsonSerializer.Serialize(response, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "hiring.upsert_todo")]
    [Description("新建或更新一个雇佣 TODO 事项（handoff item）。handoffId 相同则覆盖更新，否则新建。会话上下文由 _meta.sessionId 和 _meta.userId 自动识别。")]
    public async Task<string> UpsertTodoAsync(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("TODO 的唯一语义化 ID，格式：{阶段前缀}_{英文小写slug}。资料工单用 material_xxx，技能工单用 skill_xxx，外部系统工单用 external_xxx。同一概念必须每次使用相同 ID 以实现幂等更新，禁止使用随机 UUID。")] string handoffId,
        [Description("标题（简明描述任务内容）")] string title,
        [Description("所属阶段，如 material / skill / external")] string stage,
        [Description("目标 skill 名称")] string targetSkill,
        [Description("当前状态：drafting / ready_to_dispatch / dispatched / confirmed / needs_review / dismissed")] string status,
        [Description("任务意图或详细说明（可选）")] string? intent = null,
        [Description("分类标签（可选）")] string? category = null,
        [Description("来源说明（可选）")] string? source = null,
        [Description("验收条件（可选）")] string? acceptance = null,
        CancellationToken cancellationToken = default)
    {
        var userId = ExtractUserId(requestContext);
        var sessionId = ExtractSessionId(requestContext);
        logger.LogInformation("[MCP] hiring.upsert_todo 被调用 | handoffId={HandoffId} userId={UserId} sessionId={SessionId}", handoffId, userId ?? "<未传入>", sessionId ?? "<未传入>");

        if (userId is null)
            return """{"error":"_meta.userId 未传入，无法验证用户身份"}""";
        if (sessionId is null)
            return """{"error":"_meta.sessionId 未传入，无法定位雇佣会话"}""";

        var request = new UpsertHiringTodoRequest(
            HandoffId: handoffId,
            Title: title,
            Kind: HiringHandoffKind.HandoffTodo,
            Stage: stage,
            TargetSkill: targetSkill,
            Status: status,
            Intent: intent,
            Category: category,
            Source: source,
            Acceptance: acceptance);

        var response = await todoService.UpsertTodoAsync(sessionId, userId, request, cancellationToken);
        return JsonSerializer.Serialize(response, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "hiring.request_file_upload")]
    [Description("创建一个「请用户上传文件材料」类型的 TODO 事项，引导用户在界面上传指定文件。前端面板会自动显示上传按钮。会话上下文由 _meta.sessionId 和 _meta.userId 自动识别。")]
    public async Task<string> RequestFileUploadAsync(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("TODO 的唯一语义化 ID，格式：upload_{英文小写slug}，例如 upload_tax_report。同一文件请求必须使用相同 ID，禁止使用随机 UUID。")] string handoffId,
        [Description("请求上传的文件或材料名称")] string title,
        [Description("上传说明：描述需要用户上传什么文件以及用途")] string description,
        [Description("所属阶段，如 material / skill / external")] string stage,
        [Description("目标 skill 名称")] string targetSkill,
        [Description("验收条件：描述上传后如何验证文件合格（可选）")] string? acceptanceCriteria = null,
        CancellationToken cancellationToken = default)
    {
        var userId = ExtractUserId(requestContext);
        var sessionId = ExtractSessionId(requestContext);
        logger.LogInformation("[MCP] hiring.request_file_upload 被调用 | handoffId={HandoffId} userId={UserId} sessionId={SessionId}", handoffId, userId ?? "<未传入>", sessionId ?? "<未传入>");

        if (userId is null)
            return """{"error":"_meta.userId 未传入，无法验证用户身份"}""";
        if (sessionId is null)
            return """{"error":"_meta.sessionId 未传入，无法定位雇佣会话"}""";

        var payload = JsonSerializer.SerializeToElement(new
        {
            upload_type = "file",
            description,
            guidance = $"请上传 {title} 文件。{description}"
        });

        var request = new UpsertHiringTodoRequest(
            HandoffId: handoffId,
            Title: title,
            Kind: HiringHandoffKind.FileRequest,
            Stage: stage,
            TargetSkill: targetSkill,
            Status: HiringHandoffStatus.Drafting,
            Intent: description,
            Category: "file_upload",
            Source: "mcp_agent",
            Acceptance: acceptanceCriteria,
            Payload: payload);

        var response = await todoService.UpsertTodoAsync(sessionId, userId, request, cancellationToken);
        return JsonSerializer.Serialize(response, JsonSerializerOptions.Web);
    }

    /// <summary>从 MCP 请求上下文的 _meta 中提取 userId（Keycloak JWT sub）。</summary>
    private static string? ExtractUserId(RequestContext<CallToolRequestParams> requestContext)
        => ExtractMeta(requestContext, "userId");

    /// <summary>从 MCP 请求上下文的 _meta 中提取 sessionId（Kingcrab 传入的会话 ID）。</summary>
    private static string? ExtractSessionId(RequestContext<CallToolRequestParams> requestContext)
        => ExtractMeta(requestContext, "sessionId");

    private static string? ExtractMeta(RequestContext<CallToolRequestParams> requestContext, string key)
    {
        var meta = requestContext.Params?.Meta;
        if (meta is null)
            return null;

        if (meta.TryGetPropertyValue(key, out JsonNode? node))
            return node?.GetValue<string>();

        return null;
    }
}
