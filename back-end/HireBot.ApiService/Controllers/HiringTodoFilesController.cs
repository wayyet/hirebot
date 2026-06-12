using System.Text;
using HireBot.Abstraction;
using HireBot.ApiService.McpTools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HireBot.ApiService.Controllers;

/// <summary>
/// 雇佣 TODO 资料文件管理：仅接受 .md / .json 格式，通过 IFileStore 持久化到
/// <c>resources/todo-files/{sessionId}/{folder?}/{fileName}</c>。
/// 配合 MCP 工具 hiring.parse_uploaded_files 让大模型读取并解析。
/// </summary>
[Route("api/v1/hiring-todos/{sessionId}/files")]
[ApiController]
public sealed class HiringTodoFilesController(
    IFileStore fileStore,
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
            var virtualPath = BuildVirtualPath(sessionId, folder, safeName);

            await using var stream = file.OpenReadStream();
            var storagePath = await fileStore.SaveAsync(virtualPath, stream, cancellationToken);

            var rel = ComputeRelativePath(sessionId, folder, safeName);
            saved.Add(new UploadedFileDto(rel, file.Length, ext.TrimStart('.')));
            logger.LogInformation("[TodoFiles] 已保存 {Path} 大小={Size}B", storagePath, file.Length);
        }

        return Ok(ApiResponse<IReadOnlyList<UploadedFileDto>>.SuccessResponse(saved, "上传成功"));
    }

    /// <summary>列出会话下所有 todo 资料文件（仅元信息，不含正文）。</summary>
    [HttpGet]
    public async Task<IActionResult> List(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(ApiResponse<object>.ErrorResponse(400, "sessionId 不能为空"));

        var prefix = BuildVirtualPath(sessionId, null, null);
        var allFiles = await fileStore.ListAsync(prefix, cancellationToken);

        var items = allFiles
            .Where(e =>
            {
                var ext = Path.GetExtension(e.Path.AsSpan());
                return ext.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
            })
            .Select(e =>
            {
                var rel = ExtractRelativePath(e.Path, sessionId);
                return new UploadedFileDto(rel, e.SizeBytes, Path.GetExtension(e.Path).TrimStart('.').ToLowerInvariant());
            })
            .OrderBy(x => x.RelativePath)
            .ToList();

        return Ok(ApiResponse<IReadOnlyList<UploadedFileDto>>.SuccessResponse(items));
    }

    private static string BuildVirtualPath(string sessionId, string? folder, string? fileName)
    {
        var parts = new List<string> { "resources/todo-files", SanitizeSegment(sessionId) };
        if (!string.IsNullOrWhiteSpace(folder))
            parts.Add(SanitizeSegment(folder));
        if (!string.IsNullOrWhiteSpace(fileName))
            parts.Add(fileName);
        return string.Join("/", parts);
    }

    private static string ComputeRelativePath(string sessionId, string? folder, string fileName)
        => string.IsNullOrWhiteSpace(folder)
            ? fileName
            : $"{SanitizeSegment(folder)}/{fileName}";

    private static string ExtractRelativePath(string virtualPath, string sessionId)
    {
        var prefix = $"resources/todo-files/{SanitizeSegment(sessionId)}/";
        return virtualPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? virtualPath[prefix.Length..]
            : virtualPath;
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

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(fileName.Length);
        foreach (var ch in fileName)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        return sb.Length == 0 ? "file" : sb.ToString();
    }
}
