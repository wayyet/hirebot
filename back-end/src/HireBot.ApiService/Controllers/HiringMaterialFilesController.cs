using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.ApiService.McpTools;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HireBot.ApiService.Controllers;

/// <summary>
/// 雇佣资料阶段上传文件管理：文件内容落盘，数据库保存 hireId + sessionId 绑定元数据。
/// </summary>
[Route("api/v1/hirings/{hireId}/material-files")]
[ApiController]
public sealed class HiringMaterialFilesController(
    IWebHostEnvironment env,
    IConfiguration configuration,
    HireBotDbContext dbContext,
    ISandboxService sandboxService,
    IHttpContextAccessor httpContextAccessor,
    ILogger<HiringMaterialFilesController> logger)
    : ControllerBase
{
    private const long MaxUploadBytes = 50_000_000;
    private const string HiringSandboxRole = "hiring";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".json"
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
                    $"不支持的格式：{file.FileName}，仅允许 .md 和 .json"));
            }
        }

        var uploadContext = contextResult.Data!;
        var normalizedCategoryTitle = NormalizeOptional(requestedCategoryTitle, 160);
        var saved = new List<HiringMaterialFileDto>(nonEmptyFiles.Count);

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
        var targetPath = ResolveFilePath(uploadContext.SessionId, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var backupPath = BackupExistingFile(targetPath);
        var sha256 = string.Empty;
        var savedToDisk = false;
        var mimeType = NormalizeOptional(file.ContentType, 120);
        var existing = await dbContext.HiringMaterialFiles
            .FirstOrDefaultAsync(item =>
                item.SessionId == uploadContext.SessionId &&
                item.RelativePath == relativePath,
                cancellationToken);

        try
        {
            sha256 = await SaveFileAndComputeHashAsync(file, targetPath, cancellationToken);
            savedToDisk = true;
            var syncedWorkspaceRelativePath = await TrySyncWorkspaceCopyAsync(
                uploadContext,
                relativePath,
                safeFolder,
                safeFileName,
                targetPath,
                mimeType,
                cancellationToken);
            var workspaceRelativePath = syncedWorkspaceRelativePath ?? existing?.WorkspaceRelativePath;

            var now = DateTimeOffset.UtcNow;
            if (existing is null)
            {
                existing = new HiringMaterialFileEntity
                {
                    HireId = uploadContext.HireId,
                    SessionId = uploadContext.SessionId,
                    RelativePath = relativePath,
                    OriginalFileName = originalFileName,
                    StoragePath = targetPath,
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
                existing.StoragePath = targetPath;
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
            DeleteBackupFile(backupPath);

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
            RestoreFileAfterFailure(targetPath, backupPath, savedToDisk);
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
        var runtime = await dbContext.HiringRuntimeStates
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.HireId == normalizedHireId, cancellationToken);
        if (runtime is null)
            return ContextResult<MaterialUploadContext>.Failure(404, "雇佣上下文不存在，请重新发起流程");

        if (string.IsNullOrWhiteSpace(runtime.SessionId))
            return ContextResult<MaterialUploadContext>.Failure(409, "雇佣会话尚未就绪");

        if (!string.Equals(runtime.SessionId, normalizedSessionId, StringComparison.Ordinal))
            return ContextResult<MaterialUploadContext>.Failure(409, "session_id 与当前雇佣会话不匹配");

        var session = await dbContext.HiringSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.HireId == normalizedHireId && item.DeletedAtUtc == null, cancellationToken);
        if (session is null)
            return ContextResult<MaterialUploadContext>.Failure(404, "雇佣会话不存在");

        return ContextResult<MaterialUploadContext>.Ok(new MaterialUploadContext(
            normalizedHireId,
            normalizedSessionId,
            session.TenantId,
            session.OperatorId,
            session.OwnerSubject,
            TryResolveSandboxId(runtime.PayloadJson),
            TryResolveWorkspaceRoot(runtime.WorkflowStateJson)));
    }

    private string ResolveFilePath(string sessionId, string relativePath)
    {
        var root = Path.Combine(
            HireBotPathResolver.ResolveEvaluationResourceRoot(
                env.ContentRootPath,
                configuration["HireBot:DataRoot"],
                configuration["HireBot:EvaluationResourceRoot"]),
            HiringTodoMcpTools.TodoFilesSubdir.Replace('/', Path.DirectorySeparatorChar),
            SanitizeSegment(sessionId));
        var parts = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeSegment)
            .ToArray();
        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }

    private static async Task<string> SaveFileAndComputeHashAsync(
        IFormFile file,
        string targetPath,
        CancellationToken cancellationToken)
    {
        await using var source = file.OpenReadStream();
        await using var target = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 128,
            useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 128);
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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

    private async Task<string?> TrySyncWorkspaceCopyAsync(
        MaterialUploadContext uploadContext,
        string relativePath,
        string safeFolder,
        string safeFileName,
        string localPath,
        string? mimeType,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = NormalizeWorkspaceRoot(uploadContext.WorkspaceRoot);
        if (workspaceRoot is null)
        {
            logger.LogWarning(
                "[MaterialFiles] Workspace root unavailable, skip workspace sync. HireId={HireId} SessionId={SessionId} RelativePath={RelativePath}",
                uploadContext.HireId,
                uploadContext.SessionId,
                relativePath);
            return null;
        }

        var workspaceRootDir = TryConvertWorkspaceRootToTargetDir(workspaceRoot);
        if (workspaceRootDir is null)
        {
            logger.LogWarning(
                "[MaterialFiles] Invalid workspace root, skip workspace sync. HireId={HireId} SessionId={SessionId} WorkspaceRoot={WorkspaceRoot}",
                uploadContext.HireId,
                uploadContext.SessionId,
                workspaceRoot);
            return null;
        }

        var workspaceRelativePath = BuildWorkspaceRelativePath(relativePath);
        var targetDir = BuildWorkspaceTargetDir(workspaceRootDir, safeFolder);
        var content = await System.IO.File.ReadAllBytesAsync(localPath, cancellationToken);
        var uploadResult = await sandboxService.UploadWorkspaceFileAsync(
            new SandboxWorkspaceUploadRequestDto
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
            },
            cancellationToken);

        if (!uploadResult.Success)
        {
            logger.LogWarning(
                "[MaterialFiles] Failed to sync file into workspace. HireId={HireId} SessionId={SessionId} RelativePath={RelativePath} TargetDir={TargetDir} Message={Message}",
                uploadContext.HireId,
                uploadContext.SessionId,
                relativePath,
                targetDir,
                uploadResult.Message);
            return null;
        }

        return workspaceRelativePath;
    }

    private static string? BackupExistingFile(string targetPath)
    {
        if (!System.IO.File.Exists(targetPath)) return null;

        var backupPath = $"{targetPath}.{Guid.NewGuid():N}.bak";
        System.IO.File.Move(targetPath, backupPath);
        return backupPath;
    }

    private static void DeleteBackupFile(string? backupPath)
    {
        if (backupPath is not null && System.IO.File.Exists(backupPath))
            System.IO.File.Delete(backupPath);
    }

    private static void RestoreFileAfterFailure(string targetPath, string? backupPath, bool savedToDisk)
    {
        if (savedToDisk && System.IO.File.Exists(targetPath))
            System.IO.File.Delete(targetPath);

        if (backupPath is not null && System.IO.File.Exists(backupPath))
            System.IO.File.Move(backupPath, targetPath);
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

    private static string? TryResolveWorkspaceRoot(string workflowStateJson)
    {
        if (string.IsNullOrWhiteSpace(workflowStateJson))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<PersistedWorkflowStateProjection>(workflowStateJson, JsonOptions);
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
