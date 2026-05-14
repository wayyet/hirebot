using System.Text;
using HireBot.Abstraction;
using HireBot.ApiService.McpTools;
using HireBot.Core.Services.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace HireBot.ApiService.Controllers;

/// <summary>
/// 雇佣 TODO 资料文件管理：仅接受 .md / .json 格式，按 sessionId/folder 组织到
/// 运行数据目录下的 resources/todo-files/{sessionId}/{folder?}/{fileName}。
/// 配合 MCP 工具 hiring.parse_uploaded_files 让大模型读取并解析。
/// </summary>
[Route("api/v1/hiring-todos/{sessionId}/files")]
[ApiController]
public sealed class HiringTodoFilesController(
    IWebHostEnvironment env,
    IConfiguration configuration,
    ILogger<HiringTodoFilesController> logger)
    : ControllerBase
{
    /// <summary>上传响应：包含相对路径与字节数。</summary>
    public sealed record UploadedFileDto(string RelativePath, long SizeBytes, string Format);

    /// <summary>上传一个或多个 todo 资料文件。folder 可选：作为 sessionId 下的子文件夹分组。</summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(50_000_000)] // 50MB 总上限
    public async Task<IActionResult> UploadAsync(
        string sessionId,
        [FromForm(Name = "folder")] string? folder,
        [FromForm(Name = "files")] List<IFormFile>? files,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(ApiResponse<object>.ErrorResponse(400, "sessionId 不能为空"));
        if (files is null || files.Count == 0)
            return BadRequest(ApiResponse<object>.ErrorResponse(400, "files 不能为空"));

        var sessionDir = ResolveDir(sessionId, folder);
        Directory.CreateDirectory(sessionDir);

        var saved = new List<UploadedFileDto>(files.Count);
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".md" or ".json"))
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(
                    400, $"不支持的格式：{file.FileName}，仅允许 .md 和 .json"));
            }

            var safeName = SanitizeFileName(Path.GetFileName(file.FileName));
            var target = Path.Combine(sessionDir, safeName);
            await using var fs = System.IO.File.Create(target);
            await file.CopyToAsync(fs, cancellationToken);

            var rel = ComputeRelativePath(sessionId, folder, safeName);
            saved.Add(new UploadedFileDto(rel, file.Length, ext.TrimStart('.')));
            logger.LogInformation("[TodoFiles] 已保存 {Path} 大小={Size}B", target, file.Length);
        }

        return Ok(ApiResponse<IReadOnlyList<UploadedFileDto>>.SuccessResponse(saved, "上传成功"));
    }

    /// <summary>列出会话下所有 todo 资料文件（仅元信息，不含正文）。</summary>
    [HttpGet]
    public IActionResult List(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(ApiResponse<object>.ErrorResponse(400, "sessionId 不能为空"));

        var root = ResolveDir(sessionId, null);
        if (!Directory.Exists(root))
            return Ok(ApiResponse<IReadOnlyList<UploadedFileDto>>.SuccessResponse(Array.Empty<UploadedFileDto>(), "暂无文件"));

        var items = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".md" or ".json")
            .Select(p =>
            {
                var info = new FileInfo(p);
                var rel = Path.GetRelativePath(root, p).Replace('\\', '/');
                return new UploadedFileDto(rel, info.Length, Path.GetExtension(p).TrimStart('.').ToLowerInvariant());
            })
            .OrderBy(x => x.RelativePath)
            .ToList();

        return Ok(ApiResponse<IReadOnlyList<UploadedFileDto>>.SuccessResponse(items));
    }

    /// <summary>解析会话下所有 md/json 文件并返回正文，便于前端预览（与 MCP 工具同源逻辑）。</summary>
    [HttpGet("parse")]
    public async Task<IActionResult> ParseAsync(
        string sessionId,
        [FromQuery] int maxBytes = 200_000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(ApiResponse<object>.ErrorResponse(400, "sessionId 不能为空"));

        var root = ResolveDir(sessionId, null);
        if (!Directory.Exists(root))
            return Ok(ApiResponse<object>.SuccessResponse(new { sessionId, files = Array.Empty<object>() }));

        var files = new List<object>();
        long totalBytes = 0;
        var truncated = false;
        foreach (var p in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(s => s))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext is not (".md" or ".json")) continue;

            var rel = Path.GetRelativePath(root, p).Replace('\\', '/');
            string content;
            if (totalBytes >= maxBytes)
            {
                truncated = true;
                content = "[truncated]";
            }
            else
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(p, cancellationToken);
                var remain = maxBytes - totalBytes;
                if (bytes.LongLength > remain)
                {
                    content = Encoding.UTF8.GetString(bytes, 0, (int)remain) + "\n[... truncated]";
                    totalBytes += remain;
                    truncated = true;
                }
                else
                {
                    content = Encoding.UTF8.GetString(bytes);
                    totalBytes += bytes.LongLength;
                }
            }

            files.Add(new { relativePath = rel, sizeBytes = new FileInfo(p).Length, format = ext.TrimStart('.'), content });
        }

        return Ok(ApiResponse<object>.SuccessResponse(new { sessionId, fileCount = files.Count, truncated, files }));
    }

    private string ResolveDir(string sessionId, string? folder)
    {
        var todoRoot = Path.Combine(
            HireBotPathResolver.ResolveEvaluationResourceRoot(
                env.ContentRootPath,
                configuration["HireBot:DataRoot"],
                configuration["HireBot:EvaluationResourceRoot"]),
            HiringTodoMcpTools.TodoFilesSubdir.Replace('/', Path.DirectorySeparatorChar));
        var parts = new List<string>
        {
            todoRoot,
            SanitizeSegment(sessionId)
        };
        if (!string.IsNullOrWhiteSpace(folder))
            parts.Add(SanitizeSegment(folder));
        return Path.Combine(parts.ToArray());
    }

    private static string ComputeRelativePath(string sessionId, string? folder, string fileName)
        => string.IsNullOrWhiteSpace(folder)
            ? fileName
            : $"{SanitizeSegment(folder)}/{fileName}";

    private static string SanitizeSegment(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.') sb.Append(ch);
        }
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(fileName.Length);
        foreach (var ch in fileName)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        return sb.Length == 0 ? "file" : sb.ToString();
    }
}
