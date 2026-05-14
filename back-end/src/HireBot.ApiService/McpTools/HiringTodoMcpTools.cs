using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Hiring;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HireBot.ApiService.McpTools;

/// <summary>
/// MCP 工具：提供雇佣界面的 TODO（Handoff）事项管理 + 雇佣会话用户上传文件的解析能力。
/// userId 和 sessionId 均由 Kingcrab 通过 _meta 传入。
/// 注意：原先的 request_file_upload / request_skill_upload / request_external_config 三个工具
/// 已废弃移除，TodoPanel 现完全由 artifact 消息事件驱动阶段点亮与交互区显示。
/// </summary>
[McpServerToolType]
internal sealed class HiringTodoMcpTools(
    IHiringTodoService todoService,
    IWebHostEnvironment env,
    ILogger<HiringTodoMcpTools> logger)
{
    /// <summary>todo 上传文件的根目录，相对 wwwroot；由控制器和 MCP 工具共享。</summary>
    public const string TodoFilesSubdir = "resources/todo-files";

    [McpServerTool(Name = "hiring.list_todos", ReadOnly = true)]
    [Description("列出当前雇佣会话的所有 TODO 事项（handoff items）。会话上下文由 _meta.sessionId 和 _meta.userId 自动识别，无需传参。")]
    public async Task<string> ListTodosAsync(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var userId = ExtractUserId(requestContext);
        var sessionId = ExtractSessionId(requestContext);
        logger.LogInformation("[MCP] hiring.list_todos | userId={UserId} sessionId={SessionId}", userId ?? "<未传入>", sessionId ?? "<未传入>");

        if (userId is null) return ErrorPayload("_meta.userId 未传入，无法验证用户身份");
        if (sessionId is null) return ErrorPayload("_meta.sessionId 未传入，无法定位雇佣会话");

        var response = await todoService.GetTodosAsync(sessionId, userId, cancellationToken);
        return JsonSerializer.Serialize(response, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "hiring.upsert_todo")]
    [Description("新建或更新一个雇佣 TODO 事项（handoff item）。handoffId 相同则覆盖更新，否则新建。会话上下文由 _meta.sessionId 和 _meta.userId 自动识别。")]
    public async Task<string> UpsertTodoAsync(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("TODO 的唯一语义化 ID，格式：{阶段前缀}_{英文小写slug}。同一概念必须每次使用相同 ID 以实现幂等更新，禁止使用随机 UUID。")] string handoffId,
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
        logger.LogInformation("[MCP] hiring.upsert_todo | handoffId={HandoffId} userId={UserId} sessionId={SessionId}", handoffId, userId ?? "<未传入>", sessionId ?? "<未传入>");

        if (userId is null) return ErrorPayload("_meta.userId 未传入，无法验证用户身份");
        if (sessionId is null) return ErrorPayload("_meta.sessionId 未传入，无法定位雇佣会话");

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

    /// <summary>
    /// 解析当前雇佣会话用户已上传的 todo-files 目录，返回目录树和每个 md/json 文件的文本内容。
    /// AI 可借此读取业务资料，进行本体抽取、能力推断等下游任务。
    /// </summary>
    [McpServerTool(Name = "hiring.parse_uploaded_files", ReadOnly = true)]
    [Description("读取并解析当前雇佣会话用户已上传的所有文件（仅 .md / .json）。返回目录结构和每个文件的全文本内容，供大模型抽取本体、推断技能等。会话上下文由 _meta.sessionId 自动识别。")]
    public async Task<string> ParseUploadedFilesAsync(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("可选：限制最大返回字节数，默认 200000，避免上下文爆炸")] int maxBytes = 200_000,
        CancellationToken cancellationToken = default)
    {
        var sessionId = ExtractSessionId(requestContext);
        logger.LogInformation("[MCP] hiring.parse_uploaded_files | sessionId={SessionId} maxBytes={MaxBytes}", sessionId ?? "<未传入>", maxBytes);

        if (sessionId is null) return ErrorPayload("_meta.sessionId 未传入，无法定位雇佣会话");

        var root = ResolveSessionRoot(sessionId);
        if (!Directory.Exists(root))
        {
            return JsonSerializer.Serialize(new
            {
                session_id = sessionId,
                file_count = 0,
                files = Array.Empty<object>(),
                note = "尚未上传任何文件"
            }, JsonSerializerOptions.Web);
        }

        var files = new List<object>();
        long totalBytes = 0;
        var truncated = false;

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".md" or ".json")) continue;

            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var info = new FileInfo(path);

            string content;
            if (totalBytes >= maxBytes)
            {
                truncated = true;
                content = "[truncated: 已达 maxBytes 上限，未读取此文件正文]";
            }
            else
            {
                var remain = maxBytes - totalBytes;
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                if (bytes.LongLength > remain)
                {
                    content = Encoding.UTF8.GetString(bytes, 0, (int)remain) + "\n[... truncated]";
                    truncated = true;
                    totalBytes += remain;
                }
                else
                {
                    content = Encoding.UTF8.GetString(bytes);
                    totalBytes += bytes.LongLength;
                }
            }

            files.Add(new
            {
                relative_path = relative,
                size_bytes = info.Length,
                format = ext.TrimStart('.'),
                content
            });
        }

        return JsonSerializer.Serialize(new
        {
            session_id = sessionId,
            file_count = files.Count,
            total_bytes_read = totalBytes,
            truncated,
            files
        }, JsonSerializerOptions.Web);
    }

    private string ResolveSessionRoot(string sessionId)
    {
        var safe = SanitizeSegment(sessionId);
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        return Path.Combine(webRoot, TodoFilesSubdir.Replace('/', Path.DirectorySeparatorChar), safe);
    }

    private static string SanitizeSegment(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.') sb.Append(ch);
        }
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    private static string ErrorPayload(string message)
        => JsonSerializer.Serialize(new { error = message }, JsonSerializerOptions.Web);

    private static string? ExtractUserId(RequestContext<CallToolRequestParams> requestContext)
        => ExtractMeta(requestContext, "userId");

    private static string? ExtractSessionId(RequestContext<CallToolRequestParams> requestContext)
        => ExtractMeta(requestContext, "sessionId");

    private static string? ExtractMeta(RequestContext<CallToolRequestParams> requestContext, string key)
    {
        var meta = requestContext.Params?.Meta;
        if (meta is null) return null;
        if (meta.TryGetPropertyValue(key, out JsonNode? node)) return node?.GetValue<string>();
        return null;
    }
}
