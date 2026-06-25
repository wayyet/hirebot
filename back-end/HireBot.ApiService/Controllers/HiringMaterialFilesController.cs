using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.ApiService.McpTools;
using HireBot.ApiService.Services;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.ApiService.Controllers;

/// <summary>
/// 雇佣资料阶段上传文件管理：文件内容通过 IFileStore 持久化，数据库保存 hireId + sessionId 绑定元数据。
/// </summary>
[Route("api/v1/hirings/{hireId}/material-files")]
[ApiController]
public sealed class HiringMaterialFilesController(
    IFileStore fileStore,
    HireBotDbContext dbContext,
    ISandboxService sandboxService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<HiringMaterialFilesController> logger)
    : ControllerBase
{
    private const long MaxUploadBytes = 50_000_000;
    private const int WorkspaceSyncMaxAttempts = 3;
    private const string HiringSandboxRole = "hiring";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".json",
        ".pdf",
        ".docx",
        ".doc"
    };

    public sealed record HiringMaterialFileDto(
        Guid MaterialFileId,
        string RelativePath,
        string OriginalFileName,
        long SizeBytes,
        string Format,
        string? MimeType,
        string Sha256,
        string? RequestedCategoryTitle,
        string? WorkspaceRelativePath,
        DateTimeOffset UploadedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> UploadAsync(
        string hireId,
        [FromForm(Name = "session_id")] string? sessionId,
        [FromForm(Name = "folder")] string? folder,
        [FromForm(Name = "requested_category_title")] string? requestedCategoryTitle,
        [FromForm(Name = "files")] List<IFormFile>? files,
        CancellationToken cancellationToken)
    {
        var contextResult = await ResolveUploadContextAsync(hireId, sessionId, cancellationToken);
        if (!contextResult.Success)
            return StatusCode(contextResult.Code, ApiResponse<object>.ErrorResponse(contextResult.Code, contextResult.Message));

        if (files is null || files.Count == 0)
            return BadRequest(ApiResponse<object>.ErrorResponse(400, "files 不能为空"));

        var nonEmptyFiles = files.Where(file => file.Length > 0).ToList();
        if (nonEmptyFiles.Count == 0)
            return BadRequest(ApiResponse<object>.ErrorResponse(400, "上传文件不能为空"));

        if (nonEmptyFiles.Sum(file => file.Length) > MaxUploadBytes)
            return BadRequest(ApiResponse<object>.ErrorResponse(400, "上传文件总大小超过 50MB"));

        foreach (var file in nonEmptyFiles)
        {
            var ext = Path.GetExtension(file.FileName);
            if (!AllowedExtensions.Contains(ext))
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(
                    400,
                    $"不支持的格式：{file.FileName}，仅允许 .md / .json / .pdf / .docx / .doc"));
            }
        }

        var uploadContext = contextResult.Data!;
        var normalizedCategoryTitle = NormalizeOptional(requestedCategoryTitle, 160);
        var saved = new List<HiringMaterialFileDto>(nonEmptyFiles.Count);

        try
        {
            foreach (var file in nonEmptyFiles)
            {
                var dto = await SaveMaterialFileAsync(
                    uploadContext,
                    file,
                    folder,
                    normalizedCategoryTitle,
                    cancellationToken);
                saved.Add(dto);
            }
        }
        catch (MaterialFileUploadException ex)
        {
            logger.LogWarning(
                ex,
                "[MaterialFiles] Upload rejected because workspace sync did not complete. HireId={HireId} SessionId={SessionId}",
                uploadContext.HireId,
                uploadContext.SessionId);
            return StatusCode(ex.StatusCode, ApiResponse<object>.ErrorResponse(ex.StatusCode, ex.Message));
        }

        return Ok(ApiResponse<IReadOnlyList<HiringMaterialFileDto>>.SuccessResponse(saved, "上传成功"));
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync(
        string hireId,
        [FromQuery(Name = "session_id")] string? sessionId,
        CancellationToken cancellationToken)
    {
        var contextResult = await ResolveUploadContextAsync(hireId, sessionId, cancellationToken);
        if (!contextResult.Success)
            return StatusCode(contextResult.Code, ApiResponse<object>.ErrorResponse(contextResult.Code, contextResult.Message));

        var uploadContext = contextResult.Data!;
        var items = await dbContext.HiringMaterialFiles
            .AsNoTracking()
            .Where(item =>
                item.HireId == uploadContext.HireId &&
                item.SessionId == uploadContext.SessionId &&
                item.DeletedAtUtc == null)
            .OrderBy(item => item.RelativePath)
            .Select(item => new HiringMaterialFileDto(
                item.MaterialFileId,
                item.RelativePath,
                item.OriginalFileName,
                item.SizeBytes,
                item.Format,
                item.MimeType,
                item.Sha256,
                item.RequestedCategoryTitle,
                item.WorkspaceRelativePath,
                item.UploadedAtUtc,
                item.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<HiringMaterialFileDto>>.SuccessResponse(items));
    }

    private async Task<HiringMaterialFileDto> SaveMaterialFileAsync(
        MaterialUploadContext uploadContext,
        IFormFile file,
        string? folder,
        string? requestedCategoryTitle,
        CancellationToken cancellationToken)
    {
        var originalFileName = string.IsNullOrWhiteSpace(file.FileName)
            ? "file"
            : Path.GetFileName(file.FileName);
        var safeFileName = SanitizeFileName(originalFileName);
        var safeFolder = SanitizePath(folder);
        var relativePath = string.IsNullOrWhiteSpace(safeFolder)
            ? safeFileName
            : $"{safeFolder}/{safeFileName}";
        var virtualPath = BuildTodoFileVirtualPath(uploadContext.TenantId, uploadContext.SessionId, relativePath);

        // 先将文件内容读取到内存，计算 SHA256 哈希
        var sha256 = string.Empty;
        byte[] fileBytes;
        await using (var formStream = file.OpenReadStream())
        {
            using var memStream = new MemoryStream((int)file.Length);
            await formStream.CopyToAsync(memStream, cancellationToken);
            fileBytes = memStream.ToArray();
        }

        sha256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
        var mimeType = NormalizeOptional(file.ContentType, 120);

        string? storagePath = null;
        try
        {
            // 先保存主存储，再强制同步 workspace；同步失败时清理主存储，避免 MCP 读到半成功资料。
            using var saveStream = new MemoryStream(fileBytes);
            storagePath = await fileStore.SaveAsync(virtualPath, saveStream, cancellationToken);

            var workspaceRelativePath = await SyncWorkspaceCopyAsync(
                uploadContext,
                relativePath,
                safeFolder,
                safeFileName,
                fileBytes,
                mimeType,
                cancellationToken);

            var existing = await dbContext.HiringMaterialFiles
                .FirstOrDefaultAsync(item =>
                    item.SessionId == uploadContext.SessionId &&
                    item.RelativePath == relativePath,
                    cancellationToken);

            // 对 PDF / DOCX 文件提取文本作为伴生 .md 同步到 workspace，
            // 使 AI 能通过 hiring.parse_uploaded_files 读取二进制资料的内容。
            var ext = Path.GetExtension(safeFileName);
            if (MaterialTextExtractor.RequiresTextExtraction(ext))
            {
                await TrySyncCompanionMarkdownAsync(
                    uploadContext, safeFolder, safeFileName, storagePath, ext, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            if (existing is null)
            {
                existing = new HiringMaterialFileEntity
                {
                    HireId = uploadContext.HireId,
                    SessionId = uploadContext.SessionId,
                    RelativePath = relativePath,
                    OriginalFileName = originalFileName,
                    StoragePath = storagePath,
                    Format = Path.GetExtension(safeFileName).TrimStart('.').ToLowerInvariant(),
                    MimeType = mimeType,
                    SizeBytes = file.Length,
                    Sha256 = sha256,
                    RequestedCategoryTitle = requestedCategoryTitle,
                    WorkspaceRelativePath = workspaceRelativePath,
                    TenantId = uploadContext.TenantId,
                    OperatorId = uploadContext.OperatorId,
                    UploadedBy = uploadContext.UploadedBy,
                    UploadedAtUtc = now,
                    UpdatedAtUtc = now
                };
                dbContext.HiringMaterialFiles.Add(existing);
            }
            else
            {
                existing.HireId = uploadContext.HireId;
                existing.OriginalFileName = originalFileName;
                existing.StoragePath = storagePath;
                existing.Format = Path.GetExtension(safeFileName).TrimStart('.').ToLowerInvariant();
                existing.MimeType = mimeType;
                existing.SizeBytes = file.Length;
                existing.Sha256 = sha256;
                existing.RequestedCategoryTitle = requestedCategoryTitle;
                existing.WorkspaceRelativePath = workspaceRelativePath;
                existing.TenantId = uploadContext.TenantId;
                existing.OperatorId = uploadContext.OperatorId;
                existing.UploadedBy = uploadContext.UploadedBy;
                existing.UpdatedAtUtc = now;
                existing.DeletedAtUtc = null;
            }

            dbContext.HiringAuditLogs.Add(new HiringAuditLogEntity
            {
                SessionId = uploadContext.SessionId,
                TenantId = uploadContext.TenantId,
                HireId = uploadContext.HireId,
                Action = "upload_material_file",
                Actor = uploadContext.UploadedBy,
                Ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                BeforeSha256 = null,
                AfterSha256 = sha256,
                DetailJson = JsonSerializer.Serialize(new
                {
                    relativePath,
                    workspaceRelativePath,
                    originalFileName,
                    file.Length,
                    requestedCategoryTitle
                }, JsonSerializerOptions.Web),
                TimestampUtc = now
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "[MaterialFiles] 已保存资料文件 HireId={HireId} SessionId={SessionId} RelativePath={RelativePath} Sha256={Sha256}",
                uploadContext.HireId,
                uploadContext.SessionId,
                relativePath,
                sha256);

            return ToDto(existing);
        }
        catch
        {
            // 任一后续步骤失败时，不保留本次主存储文件，避免后续解析拿到缺少 source_path 的脏数据。
            if (!string.IsNullOrWhiteSpace(storagePath))
            {
                try
                {
                    await fileStore.DeleteAsync(storagePath, cancellationToken);
                }
                catch (Exception cleanupEx)
                {
                    logger.LogWarning(
                        cleanupEx,
                        "[MaterialFiles] Failed to cleanup material file after upload failure. HireId={HireId} SessionId={SessionId} StoragePath={StoragePath}",
                        uploadContext.HireId,
                        uploadContext.SessionId,
                        storagePath);
                }
            }
            throw;
        }
    }

    private async Task<ContextResult<MaterialUploadContext>> ResolveUploadContextAsync(
        string hireId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hireId))
            return ContextResult<MaterialUploadContext>.Failure(400, "hireId 不能为空");

        if (string.IsNullOrWhiteSpace(sessionId))
            return ContextResult<MaterialUploadContext>.Failure(400, "session_id 不能为空");

        var normalizedHireId = hireId.Trim();
        var normalizedSessionId = sessionId.Trim();

        var session = await dbContext.HiringSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.HireId == normalizedHireId && item.DeletedAtUtc == null, cancellationToken);

        if (session is null)
            return ContextResult<MaterialUploadContext>.Failure(404, "雇佣会话不存在，请重新发起流程");

        if (!string.Equals(session.SessionId, normalizedSessionId, StringComparison.Ordinal))
            return ContextResult<MaterialUploadContext>.Failure(409, "session_id 与当前雇佣会话不匹配");

        var tenantId = session.TenantId ?? dbContext.TenantId ?? "default";
        // Recover the workspace root recorded during template bootstrap so uploads can be mirrored into the session workspace.
        var progress = await dbContext.HiringStageProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.HireId == normalizedHireId, cancellationToken);

        var sandbox = await dbContext.SandboxInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ScopeType == "Hire" && s.ScopeKey == normalizedHireId, cancellationToken);
        var workspaceRoot = TryResolveWorkspaceRoot(sandbox?.Metadata) ??
            TryResolveWorkspaceRoot(progress?.UploadedFilesJson);

        return ContextResult<MaterialUploadContext>.Ok(new MaterialUploadContext(
            normalizedHireId,
            normalizedSessionId,
            tenantId,
            session.OperatorId,
            session.OwnerSubject,
            sandbox?.SandboxId ?? "",
            workspaceRoot));
    }

    private static HiringMaterialFileDto ToDto(HiringMaterialFileEntity entity)
        => new(
            entity.MaterialFileId,
            entity.RelativePath,
            entity.OriginalFileName,
            entity.SizeBytes,
            entity.Format,
            entity.MimeType,
            entity.Sha256,
            entity.RequestedCategoryTitle,
            entity.WorkspaceRelativePath,
            entity.UploadedAtUtc,
            entity.UpdatedAtUtc);

    private async Task<string> SyncWorkspaceCopyAsync(
        MaterialUploadContext uploadContext,
        string relativePath,
        string safeFolder,
        string safeFileName,
        byte[] content,
        string? mimeType,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = NormalizeWorkspaceRoot(uploadContext.WorkspaceRoot);
        if (workspaceRoot is null)
        {
            throw new MaterialFileUploadException(
                StatusCodes.Status409Conflict,
                "资料上传失败：当前雇佣会话尚未准备好沙箱工作区，请刷新页面或稍后重试上传。");
        }

        var workspaceRootDir = TryConvertWorkspaceRootToTargetDir(workspaceRoot);
        if (workspaceRootDir is null)
        {
            throw new MaterialFileUploadException(
                StatusCodes.Status409Conflict,
                "资料上传失败：当前雇佣会话的沙箱工作区路径无效，请刷新页面或重新进入雇佣流程后重试上传。");
        }

        var workspaceRelativePath = BuildWorkspaceRelativePath(relativePath);
        var targetDir = BuildWorkspaceTargetDir(workspaceRootDir, safeFolder);
        var request = new SandboxWorkspaceUploadRequestDto
        {
            ScopeType = SandboxScopeTypes.Hire,
            ScopeKey = uploadContext.HireId,
            SandboxRole = HiringSandboxRole,
            OwnerSubject = uploadContext.UploadedBy,
            SandboxId = uploadContext.SandboxId,
            TargetDir = targetDir,
            FileName = safeFileName,
            Content = content,
            ContentType = ResolveContentType(safeFileName, mimeType)
        };

        for (var attempt = 1; attempt <= WorkspaceSyncMaxAttempts; attempt++)
        {
            var uploadResult = await sandboxService.UploadWorkspaceFileAsync(request, cancellationToken);
            if (uploadResult.Success)
            {
                return workspaceRelativePath;
            }

            logger.LogWarning(
                "[MaterialFiles] Failed to sync file into workspace. Attempt={Attempt}/{MaxAttempts} HireId={HireId} SessionId={SessionId} RelativePath={RelativePath} TargetDir={TargetDir} Message={Message}",
                attempt,
                WorkspaceSyncMaxAttempts,
                uploadContext.HireId,
                uploadContext.SessionId,
                relativePath,
                targetDir,
                uploadResult.Message);

            if (attempt == WorkspaceSyncMaxAttempts)
            {
                throw new MaterialFileUploadException(
                    StatusCodes.Status503ServiceUnavailable,
                    "资料上传失败：资料未能同步到沙箱工作区，请稍后重试上传。");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
        }

        throw new MaterialFileUploadException(
            StatusCodes.Status503ServiceUnavailable,
            "资料上传失败：资料未能同步到沙箱工作区，请稍后重试上传。");
    }

    /// <summary>
    /// 对 PDF / DOCX 文件提取文本并落盘为伴生 .md。
    /// 伴生文件通过 IFileStore 持久化（与原始文件同路径），
    /// 同时尝试同步到 sandbox workspace（workspaceRoot 不可用时仅跳过，不报错）。
    /// 失败时仅记录日志，不阻断主上传流程。
    /// </summary>
    private async Task TrySyncCompanionMarkdownAsync(
        MaterialUploadContext uploadContext,
        string safeFolder,
        string safeFileName,
        string storagePath,
        string ext,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var readStream = await fileStore.OpenReadAsync(storagePath, cancellationToken);
            var extractedText = MaterialTextExtractor.ExtractText(readStream, ext);
            if (string.IsNullOrWhiteSpace(extractedText)) return;

            var companionFileName = MaterialTextExtractor.BuildCompanionMarkdownFileName(safeFileName);
            var companionContent = Encoding.UTF8.GetBytes(extractedText);

            // 1) 通过 IFileStore 落盘，与原始文件同路径
            var companionRelativePath = string.IsNullOrWhiteSpace(safeFolder)
                ? companionFileName
                : $"{safeFolder}/{companionFileName}";
            var companionVirtualPath = BuildTodoFileVirtualPath(uploadContext.TenantId, uploadContext.SessionId, companionRelativePath);
            using (var companionStream = new MemoryStream(companionContent))
            {
                await fileStore.SaveAsync(companionVirtualPath, companionStream, cancellationToken);
            }

            // 2) 尝试同步到 sandbox workspace（best-effort）
            var workspaceRoot = NormalizeWorkspaceRoot(uploadContext.WorkspaceRoot);
            if (workspaceRoot is not null)
            {
                var workspaceRootDir = TryConvertWorkspaceRootToTargetDir(workspaceRoot);
                if (workspaceRootDir is not null)
                {
                    var targetDir = BuildWorkspaceTargetDir(workspaceRootDir, safeFolder);
                    await sandboxService.UploadWorkspaceFileAsync(
                        new SandboxWorkspaceUploadRequestDto
                        {
                            ScopeType = SandboxScopeTypes.Hire,
                            ScopeKey = uploadContext.HireId,
                            SandboxRole = HiringSandboxRole,
                            OwnerSubject = uploadContext.UploadedBy,
                            SandboxId = uploadContext.SandboxId,
                            TargetDir = targetDir,
                            FileName = companionFileName,
                            Content = companionContent,
                            ContentType = "text/markdown"
                        },
                        cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[MaterialFiles] Companion .md extraction/sync failed. HireId={HireId} File={FileName}",
                uploadContext.HireId, safeFileName);
        }
    }

    private static string SanitizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var segments = value.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeSegment)
            .Where(segment => segment.Length > 0)
            .ToArray();
        return string.Join('/', segments);
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

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? TryResolveSandboxId(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<PersistedHiringMetaProjection>(payloadJson, JsonOptions);
            return NormalizeOptional(payload?.SandboxId, 128);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryResolveWorkspaceRoot(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null ||
            !metadata.TryGetValue(SandboxMetaKeys.HiringWorkspaceRoot, out var workspaceRoot))
        {
            return null;
        }

        return NormalizeWorkspaceRoot(workspaceRoot);
    }

    private static string? TryResolveWorkspaceRoot(string? uploadedFilesJson)
    {
        if (string.IsNullOrWhiteSpace(uploadedFilesJson))
        {
            return null;
        }
        try
        {
            var uploadedFiles = JsonSerializer.Deserialize<IReadOnlyList<PersistedChatFileDto>>(uploadedFilesJson, JsonOptions);
            if (uploadedFiles is not null)
            {
                for (var index = uploadedFiles.Count - 1; index >= 0; index--)
                {
                    var metadata = uploadedFiles[index].Metadata;
                    if (metadata is null ||
                        !metadata.TryGetValue("workspaceDir", out var workspaceDir) ||
                        string.IsNullOrWhiteSpace(workspaceDir))
                    {
                        continue;
                    }
                    var normalized = NormalizeWorkspaceRoot(workspaceDir);
                    if (normalized is not null)
                    {
                        return normalized;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Fall back to the legacy persisted shape for older runtime-state payloads.
        }
        try
        {
            var state = JsonSerializer.Deserialize<PersistedWorkflowStateProjection>(uploadedFilesJson, JsonOptions);
            if (state?.Materials is null)
            {
                return null;
            }
            for (var index = state.Materials.Count - 1; index >= 0; index--)
            {
                var material = state.Materials[index];
                if (material.Metadata is null ||
                    !material.Metadata.TryGetValue("workspaceDir", out var workspaceDir) ||
                    string.IsNullOrWhiteSpace(workspaceDir))
                {
                    continue;
                }
                var normalized = NormalizeWorkspaceRoot(workspaceDir);
                if (normalized is not null)
                {
                    return normalized;
                }
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
    private static string? NormalizeWorkspaceRoot(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }

        var trimmed = workspaceRoot.Trim().TrimEnd('/');
        return trimmed.StartsWith("/workspace/", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : null;
    }

    private static string? TryConvertWorkspaceRootToTargetDir(string workspaceRoot)
    {
        if (!workspaceRoot.StartsWith("/workspace/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = workspaceRoot["/workspace/".Length..].Trim('/');
        return relative.Length == 0 ? null : relative;
    }

    private static string BuildWorkspaceRelativePath(string relativePath)
        => $"uploads/materials/{relativePath.Replace('\\', '/').Trim('/')}";

    private static string BuildTodoFileVirtualPath(string tenantId, string sessionId, string relativePath)
        => ArtifactStoragePaths.BuildTodoFilePath(tenantId, sessionId, relativePath);

    private static string BuildWorkspaceTargetDir(string workspaceRootDir, string safeFolder)
    {
        var normalizedRoot = workspaceRootDir.Trim('/');
        if (string.IsNullOrWhiteSpace(safeFolder))
        {
            return $"{normalizedRoot}/uploads/materials";
        }

        return $"{normalizedRoot}/uploads/materials/{safeFolder}";
    }

    private static string ResolveContentType(string safeFileName, string? mimeType)
    {
        if (!string.IsNullOrWhiteSpace(mimeType))
        {
            return mimeType;
        }

        return Path.GetExtension(safeFileName).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            _ => "text/markdown"
        };
    }

    private sealed record MaterialUploadContext(
        string HireId,
        string SessionId,
        string TenantId,
        string OperatorId,
        string UploadedBy,
        string? SandboxId,
        string? WorkspaceRoot);

    private sealed class MaterialFileUploadException(int statusCode, string message) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }

    private sealed record PersistedHiringMetaProjection(
        string? SandboxId);

    private sealed record PersistedWorkflowStateProjection(
        IReadOnlyList<HiringConversationMaterialDto>? Materials);

    private sealed record ContextResult<T>(bool Success, int Code, string Message, T? Data)
    {
        public static ContextResult<T> Ok(T data) => new(true, 200, "操作成功", data);

        public static ContextResult<T> Failure(int code, string message) => new(false, code, message, default);
    }
}
