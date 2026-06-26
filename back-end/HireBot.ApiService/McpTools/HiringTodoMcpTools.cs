using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HireBot.Abstraction;
using HireBot.Abstraction.Infrastructure.Multitenancy;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HireBot.ApiService.McpTools;

[McpServerToolType]
internal sealed class HiringTodoMcpTools(
    IFileStore fileStore,
    HireBotDbContext dbContext,
    ITenantContextProvider tenantContextProvider,
    ILogger<HiringTodoMcpTools> logger)
{
    public const string TodoFilesSubdir = "todo-files";

    [McpServerTool(Name = "hiring.parse_uploaded_files", ReadOnly = true)]
    [Description("读取当前雇佣会话已上传的 .md / .json 资料全文，并返回与资料条目对应的 source_path 元数据。")]
    public async Task<string> ParseUploadedFilesAsync(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("可选：限制最大返回字节数，默认 200000。")] int maxBytes = 200_000,
        CancellationToken cancellationToken = default)
    {
        var sessionId = ExtractSessionId(requestContext);
        logger.LogInformation(
            "[MCP] hiring.parse_uploaded_files | sessionId={SessionId} maxBytes={MaxBytes}",
            sessionId ?? "<missing>",
            maxBytes);

        if (sessionId is null)
        {
            return ErrorPayload("_meta.sessionId 未传入，无法定位雇佣会话");
        }

        return await ParseUploadedFilesForSessionAsync(sessionId, maxBytes, cancellationToken);
    }

    internal async Task<string> ParseUploadedFilesForSessionAsync(
        string sessionId,
        int maxBytes = 200_000,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContextProvider.GetTenantId() ?? dbContext.TenantId;
        var directoryPrefix = BuildTodoFilesDirectoryPrefix(tenantId, sessionId);
        var allEntries = await fileStore.ListAsync(directoryPrefix, cancellationToken);

        if (allEntries.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                session_id = sessionId,
                file_count = 0,
                files = Array.Empty<object>(),
                note = "尚未上传任何文件"
            }, JsonSerializerOptions.Web);
        }

        var fileMetadata = await dbContext.HiringMaterialFiles
            .AsNoTracking()
            .Where(item => item.SessionId == sessionId && item.DeletedAtUtc == null)
            .Select(item => new UploadedFileMetadata(
                item.RelativePath,
                item.OriginalFileName,
                item.RequestedCategoryTitle,
                item.WorkspaceRelativePath))
            .ToDictionaryAsync(item => item.RelativePath, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var files = new List<object>();
        long totalBytes = 0;
        var truncated = false;

        foreach (var entry in allEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(entry.Path);
            if (!ext.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = ExtractRelativePath(entry.Path, directoryPrefix);
            var metadata = fileMetadata.GetValueOrDefault(relative);
            var format = ext.TrimStart('.').ToLowerInvariant();

            string content;
            if (totalBytes >= maxBytes)
            {
                truncated = true;
                content = "[truncated: maxBytes limit reached]";
            }
            else
            {
                var remain = maxBytes - totalBytes;
                await using var stream = await fileStore.OpenReadAsync(entry.Path, cancellationToken);
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream, cancellationToken);
                var bytes = memoryStream.ToArray();

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
                original_file_name = metadata?.OriginalFileName,
                requested_category_title = metadata?.RequestedCategoryTitle,
                source_path = metadata?.WorkspaceRelativePath,
                size_bytes = entry.SizeBytes,
                format,
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

    private static string ExtractRelativePath(string virtualPath, string directoryPrefix)
    {
        var prefix = directoryPrefix.TrimEnd('/') + "/";
        return virtualPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? virtualPath[prefix.Length..]
            : virtualPath;
    }

    private static string BuildTodoFilesDirectoryPrefix(string? tenantId, string sessionId)
    {
        var tenantSegment = string.IsNullOrWhiteSpace(tenantId) ? "tenant" : SanitizeSegment(tenantId);
        return $"{ArtifactStoragePaths.ProjectRoot}/{tenantSegment}/resources/todo-files/{SanitizeSegment(sessionId)}";
    }

    private static string SanitizeSegment(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                sb.Append(ch);
            }
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
        if (meta is null)
        {
            return null;
        }

        if (meta.TryGetPropertyValue(key, out JsonNode? node))
        {
            return node?.GetValue<string>();
        }

        return null;
    }

    private sealed record UploadedFileMetadata(
        string RelativePath,
        string OriginalFileName,
        string? RequestedCategoryTitle,
        string? WorkspaceRelativePath);
}
