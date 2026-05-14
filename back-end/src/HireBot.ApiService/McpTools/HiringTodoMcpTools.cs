using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HireBot.ApiService.McpTools;

/// <summary>
/// MCP 工具：仅保留雇佣会话用户上传文件的解析能力。
/// 新版右侧 TODO 面板由 artifact (material_collection_progress / skill_workorder_progress /
/// external_workorder_progress 等) 事件驱动阶段亮灯与交互区显示，不再使用 handoff 文本工单，
/// 因此 hiring.list_todos / hiring.upsert_todo / hiring.request_* 等旧工具全部下线。
/// userId 和 sessionId 由 Kingcrab 通过 _meta 传入。
/// </summary>
[McpServerToolType]
internal sealed class HiringTodoMcpTools(
    IWebHostEnvironment env,
    ILogger<HiringTodoMcpTools> logger)
{
    /// <summary>todo 上传文件的根目录，相对 wwwroot；由控制器和 MCP 工具共享。</summary>
    public const string TodoFilesSubdir = "resources/todo-files";

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
