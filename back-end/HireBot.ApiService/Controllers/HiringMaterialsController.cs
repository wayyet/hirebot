using System.Security.Cryptography;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Core.Services.Internal;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HireBot.ApiService.Controllers;

[Route("api/v1/hirings/{hireId}/materials")]
[ApiController]
public sealed class HiringMaterialsController(
    HireBotDbContext dbContext,
    IFileStore fileStore,
    IHttpContextAccessor httpContextAccessor) : ControllerBase
{
    public sealed class UploadMaterialForm
    {
        [FromForm(Name = "file")]
        public required IFormFile File { get; init; }
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(250_000_000)] // 250MB
    public async Task<IActionResult> UploadMaterial(
        string hireId,
        [FromForm] UploadMaterialForm formModel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hireId))
        {
            return BadRequest(ApiResponse<HiringConversationMaterialDto>.ErrorResponse(400, "hireId 不能为空"));
        }

        var file = formModel?.File;
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiResponse<HiringConversationMaterialDto>.ErrorResponse(400, "文件为空"));
        }

        var session = await dbContext.HiringSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.HireId == hireId.Trim(), cancellationToken);
        if (session is null)
        {
            return NotFound(ApiResponse<HiringConversationMaterialDto>.ErrorResponse(404, "雇佣会话不存在"));
        }

        if (string.IsNullOrWhiteSpace(session.TenantId))
        {
            return StatusCode(StatusCodes.Status409Conflict,
                ApiResponse<HiringConversationMaterialDto>.ErrorResponse(409, "雇佣会话缺少租户信息，无法保存资料"));
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        var type = form.TryGetValue("type", out var typeValue) && !string.IsNullOrWhiteSpace(typeValue)
            ? typeValue.ToString().Trim()
            : "file";

        var originalName = string.IsNullOrWhiteSpace(file.FileName) ? "upload.bin" : Path.GetFileName(file.FileName);
        var safeType = ArtifactStoragePaths.Sanitize(type);
        var safeFileName = ArtifactStoragePaths.Sanitize(originalName);
        var category = $"materials/{safeType}";

        string sha256;
        await using (var stream = file.OpenReadStream())
        {
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        await using var stream2 = file.OpenReadStream();
        var storagePath = await fileStore.SaveAsync(
            $"{ArtifactStoragePaths.ProjectRoot}/{ArtifactStoragePaths.Sanitize(session.TenantId)}/sessions/{ArtifactStoragePaths.Sanitize(session.SessionId)}/{category}/{safeFileName}",
            stream2,
            cancellationToken);

        dbContext.HiringArtifacts.Add(new HiringArtifactEntity
        {
            SessionId = session.SessionId,
            TenantId = session.TenantId,
            Kind = "intermediate",
            LogicalPath = $"{category}/{safeFileName}",
            FileName = safeFileName,
            SizeBytes = file.Length,
            Sha256 = sha256,
            StoragePath = storagePath,
            IsFinal = false,
            IsArchived = false,
            UploadedAtUtc = DateTimeOffset.UtcNow
        });

        var metadata = form.Keys
            .Where(key => !string.Equals(key, "file", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(key => key, key => form[key].ToString(), StringComparer.OrdinalIgnoreCase);
        metadata["storagePath"] = storagePath;
        metadata["sha256"] = sha256;

        dbContext.HiringAuditLogs.Add(new HiringAuditLogEntity
        {
            SessionId = session.SessionId,
            TenantId = session.TenantId,
            HireId = session.HireId,
            Action = "upload_material",
            Actor = session.OwnerSubject,
            Ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            BeforeSha256 = null,
            AfterSha256 = sha256,
            DetailJson = JsonSerializer.Serialize(new { type, name = originalName, size = file.Length, storagePath }),
            TimestampUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var material = new HiringConversationMaterialDto
        {
            Type = type,
            Name = safeFileName,
            Content = null,
            ContentHash = sha256,
            Size = file.Length,
            MimeType = string.IsNullOrWhiteSpace(file.ContentType) ? null : file.ContentType,
            Metadata = metadata
        };

        return Ok(ApiResponse<HiringConversationMaterialDto>.SuccessResponse(material));
    }
}

