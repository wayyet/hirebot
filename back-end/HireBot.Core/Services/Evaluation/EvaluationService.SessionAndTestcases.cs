using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Evaluation.Tools;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Evaluation;

internal sealed partial class EvaluationService
{
    private async Task StartNewEvaluationSessionAsync(
        string scope,
        string employeeId,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var latestIteration = await dbContext.EvaluationSessions
            .AsNoTracking()
            .Where(item =>
                item.OwnerSubject == scope &&
                item.EmployeeId == employeeId)
            .Select(item => (int?)item.Iteration)
            .MaxAsync(cancellationToken) ?? 0;

        var session = new EvaluationSessionEntity
        {
            Id = Guid.NewGuid(),
            SessionId = BuildEvaluationSessionId(),
            OwnerSubject = scope,
            EmployeeId = employeeId,
            TargetHireId = workspaceContext.TargetHireId,
            TargetSandboxId = workspaceContext.TargetSandboxId,
            EvaluatorHireId = workspaceContext.EvaluatorHireId,
            EvaluatorSandboxId = workspaceContext.EvaluatorSandboxId,
            Status = "ready",
            Iteration = latestIteration + 1,
            LastError = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.EvaluationSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<EvaluationSessionEntity> GetOrCreateSessionEntityAsync(
        string scope,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var latestSession = await dbContext.EvaluationSessions
            .Where(item =>
                item.OwnerSubject == scope &&
                item.EmployeeId == employee.EmployeeId)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestSession is null ||
            !string.Equals(latestSession.TargetHireId, workspaceContext.TargetHireId, StringComparison.OrdinalIgnoreCase))
        {
            var created = new EvaluationSessionEntity
            {
                Id = Guid.NewGuid(),
                SessionId = BuildEvaluationSessionId(),
                OwnerSubject = scope,
                EmployeeId = employee.EmployeeId,
                TargetHireId = workspaceContext.TargetHireId,
                TargetSandboxId = workspaceContext.TargetSandboxId,
                EvaluatorHireId = workspaceContext.EvaluatorHireId,
                EvaluatorSandboxId = workspaceContext.EvaluatorSandboxId,
                Status = "ready",
                Iteration = 1,
                LastError = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.EvaluationSessions.Add(created);
            await dbContext.SaveChangesAsync(cancellationToken);
            return created;
        }

        var changed = false;
        if (!string.Equals(latestSession.TargetSandboxId, workspaceContext.TargetSandboxId, StringComparison.OrdinalIgnoreCase))
        {
            latestSession.TargetSandboxId = workspaceContext.TargetSandboxId;
            changed = true;
        }

        if (!string.Equals(latestSession.EvaluatorHireId, workspaceContext.EvaluatorHireId, StringComparison.OrdinalIgnoreCase))
        {
            latestSession.EvaluatorHireId = workspaceContext.EvaluatorHireId;
            changed = true;
        }

        if (!string.Equals(latestSession.EvaluatorSandboxId, workspaceContext.EvaluatorSandboxId, StringComparison.OrdinalIgnoreCase))
        {
            latestSession.EvaluatorSandboxId = workspaceContext.EvaluatorSandboxId;
            changed = true;
        }

        if (changed)
        {
            latestSession.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return latestSession;
    }

    private async Task UpdateSessionStatusAsync(
        EvaluationSessionEntity sessionEntity,
        string status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        sessionEntity.Status = string.IsNullOrWhiteSpace(status) ? sessionEntity.Status : status.Trim().ToLowerInvariant();
        sessionEntity.LastError = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
        sessionEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<EvaluationAssetEntity> PersistTextAssetAsync(
        EvaluationSessionEntity sessionEntity,
        string assetType,
        string relatedKey,
        string fileName,
        string content,
        string mimeType,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId(sessionEntity);
        var stored = await evaluationAssetStore.SaveTextAsync(
            tenantId,
            sessionEntity.SessionId,
            sessionEntity.Iteration,
            assetType,
            fileName,
            content,
            mimeType,
            cancellationToken);

        var entity = new EvaluationAssetEntity
        {
            Id = Guid.NewGuid(),
            SessionEntityId = sessionEntity.Id,
            AssetType = NormalizeAssetType(assetType),
            RelatedKey = string.IsNullOrWhiteSpace(relatedKey) ? null : relatedKey.Trim(),
            RelativePath = stored.RelativePath,
            PublicUrl = stored.PublicUrl,
            MimeType = stored.MimeType,
            Size = stored.Size,
            ContentHash = stored.ContentHash,
            SourceType = string.IsNullOrWhiteSpace(sourceType) ? "system" : sourceType.Trim().ToLowerInvariant(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.EvaluationAssets.Add(entity);
        sessionEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private async Task<EvaluationAssetEntity> PersistBinaryAssetAsync(
        EvaluationSessionEntity sessionEntity,
        string assetType,
        string relatedKey,
        string fileName,
        byte[] content,
        string mimeType,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId(sessionEntity);
        var stored = await evaluationAssetStore.SaveBytesAsync(
            tenantId,
            sessionEntity.SessionId,
            sessionEntity.Iteration,
            assetType,
            fileName,
            content,
            mimeType,
            cancellationToken);

        var entity = new EvaluationAssetEntity
        {
            Id = Guid.NewGuid(),
            SessionEntityId = sessionEntity.Id,
            AssetType = NormalizeAssetType(assetType),
            RelatedKey = string.IsNullOrWhiteSpace(relatedKey) ? null : relatedKey.Trim(),
            RelativePath = stored.RelativePath,
            PublicUrl = stored.PublicUrl,
            MimeType = stored.MimeType,
            Size = stored.Size,
            ContentHash = stored.ContentHash,
            SourceType = string.IsNullOrWhiteSpace(sourceType) ? "system" : sourceType.Trim().ToLowerInvariant(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.EvaluationAssets.Add(entity);
        sessionEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private static string RequireTenantId(EvaluationSessionEntity sessionEntity)
    {
        if (string.IsNullOrWhiteSpace(sessionEntity.TenantId))
        {
            throw new InvalidOperationException($"evaluation session '{sessionEntity.SessionId}' is missing tenant id.");
        }

        return sessionEntity.TenantId.Trim();
    }

    private async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesAsync(
        EvaluationWorkspaceContext workspaceContext,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var fromArtifactPackage = await LoadTestcaseSourcesFromArtifactPackageAsync(
            workspaceContext,
            employee,
            cancellationToken);
        if (fromArtifactPackage.Count > 0)
        {
            return fromArtifactPackage;
        }

        var fromEvaluatorTemplatePackage = await LoadTestcaseSourcesFromEvaluatorTemplatePackageAsync(
            workspaceContext.EvaluatorTemplatePackageZipPath,
            "evaluator-template-package",
            cancellationToken);
        if (fromEvaluatorTemplatePackage.Count > 0)
        {
            return fromEvaluatorTemplatePackage;
        }

        var fromUploadedTemplatePackage = await LoadTestcaseSourcesFromEvaluatorTemplatePackageAsync(
            workspaceContext.UploadedTemplatePackageZipPath,
            "uploaded-template-package",
            cancellationToken);
        if (fromUploadedTemplatePackage.Count > 0)
        {
            logger.LogInformation(
                "[Eval] Fallback to uploaded template package testcase files. UploadedTemplatePackageZipPath={UploadedTemplatePackageZipPath}",
                workspaceContext.UploadedTemplatePackageZipPath);
            return fromUploadedTemplatePackage;
        }

        var fromTemplateDefinition = await LoadTestcaseSourcesFromTemplateDefinitionAsync(employee, cancellationToken);
        if (fromTemplateDefinition.Count > 0)
        {
            logger.LogInformation(
                "[Eval] Fallback to template definition testcase files. EmployeeId={EmployeeId}, TemplateId={TemplateId}",
                employee.EmployeeId,
                employee.SourceTemplateId);
            return fromTemplateDefinition;
        }

        logger.LogWarning(
            "[Eval] No testcase json found under testcases/ in evaluator template package. TemplatePackageZipPath={TemplatePackageZipPath}",
            workspaceContext.EvaluatorTemplatePackageZipPath);
        return [];
    }

    private async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromArtifactPackageAsync(
        EvaluationWorkspaceContext workspaceContext,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var candidateHireIds = new[]
            {
                workspaceContext.TargetHireId,
                employee.EmployeeId
            }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var hireId in candidateHireIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageSnapshot = await artifactPackageService.GetPackageByKindAsync(
                hireId,
                HiringArtifactPackageKinds.FinalPackageZip,
                cancellationToken);
            if (packageSnapshot?.Content is not { Length: > 0 })
            {
                continue;
            }

            var sourceType = $"artifact-package:{HiringArtifactPackageKinds.FinalPackageZip}";
            var sources = await LoadTestcaseSourcesFromZipBytesAsync(
                packageSnapshot.Content,
                sourceType,
                packageSnapshot.FileName,
                cancellationToken);
            if (sources.Count == 0)
            {
                continue;
            }

            logger.LogInformation(
                "[Eval] Loaded testcase sources from final hiring artifact package. HireId={HireId}, Kind={Kind}, FileName={FileName}, Count={Count}",
                hireId,
                packageSnapshot.Kind,
                packageSnapshot.FileName,
                sources.Count);
            return sources;
        }

        return [];
    }

    private async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromTemplateDefinitionAsync(
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var templateId = employee.SourceTemplateId?.Trim();
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return [];
        }

        TemplatePackageDefinition templatePackage;
        try
        {
            templatePackage = await templatePackageProvider.LoadAsync(templateId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[Eval] Failed to load template package for testcase sources. EmployeeId={EmployeeId}, TemplateId={TemplateId}",
                employee.EmployeeId,
                templateId);
            return [];
        }

        if (templatePackage.PackageFiles.Count == 0)
        {
            return [];
        }

        var sources = new List<TestcaseSourceFile>(templatePackage.PackageFiles.Count);
        foreach (var packageFile in templatePackage.PackageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTemplateTestcaseEntry(packageFile.RelativePath))
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(packageFile.Content);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            var normalizedPath = NormalizeZipEntryPath(packageFile.RelativePath);
            sources.Add(new TestcaseSourceFile(
                FileName: Path.GetFileName(normalizedPath),
                SourcePath: normalizedPath,
                RawJson: json,
                SourceType: "template-definition"));
        }

        return sources;
    }

    private async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromEvaluatorTemplatePackageAsync(
        string? templatePackageZipPath,
        string sourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(templatePackageZipPath))
        {
            return [];
        }

        var normalizedZipPath = templatePackageZipPath.Trim();
        if (!File.Exists(normalizedZipPath))
        {
            logger.LogWarning(
                "[Eval] Template package zip does not exist. SourceType={SourceType}, TemplatePackageZipPath={TemplatePackageZipPath}",
                sourceType,
                normalizedZipPath);
            return [];
        }

        var sources = new List<TestcaseSourceFile>();
        try
        {
            await using var fileStream = new FileStream(normalizedZipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            sources.AddRange(await LoadTestcaseSourcesFromZipStreamAsync(
                fileStream,
                sourceType,
                cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[Eval] Failed to extract testcase files from template package. SourceType={SourceType}, TemplatePackageZipPath={TemplatePackageZipPath}",
                sourceType,
                normalizedZipPath);
        }

        return sources;
    }

    private async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromZipBytesAsync(
        byte[] archiveBytes,
        string sourceType,
        string sourceName,
        CancellationToken cancellationToken)
    {
        if (archiveBytes.Length == 0)
        {
            return [];
        }

        try
        {
            using var memoryStream = new MemoryStream(archiveBytes, writable: false);
            return await LoadTestcaseSourcesFromZipStreamAsync(
                memoryStream,
                sourceType,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[Eval] Failed to extract testcase files from artifact package. SourceType={SourceType}, SourceName={SourceName}",
                sourceType,
                sourceName);
            return [];
        }
    }

    private static async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromZipStreamAsync(
        Stream archiveStream,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var sources = new List<TestcaseSourceFile>();
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTemplateTestcaseEntry(entry.FullName))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            var json = await reader.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            var normalizedEntryPath = NormalizeZipEntryPath(entry.FullName);
            sources.Add(new TestcaseSourceFile(
                FileName: Path.GetFileName(normalizedEntryPath),
                SourcePath: normalizedEntryPath,
                RawJson: json,
                SourceType: sourceType));
        }

        return sources;
    }

    private static bool IsTemplateTestcaseEntry(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath))
        {
            return false;
        }

        var normalizedPath = NormalizeZipEntryPath(entryPath);
        if (!normalizedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Path.GetFileName(normalizedPath)))
        {
            return false;
        }

        // 只采集模板包 testcases/ 目录下的 json，避免混入其它来源。
        return normalizedPath.StartsWith("testcases/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/testcases/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeZipEntryPath(string value)
    {
        return value.Replace('\\', '/').TrimStart('/');
    }

    private static async Task<TargetArtifactBundle> ZipDirectoryAsBundleAsync(
        string sourceDirectory,
        string fileName,
        string sourceType,
        CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .Where(file => ShouldIncludeBundleFile(sourceDirectory, file))
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream, cancellationToken);
            }
        }

        var bytes = memoryStream.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new TargetArtifactBundle(
            FileName: string.IsNullOrWhiteSpace(fileName) ? "hiring-artifacts.zip" : fileName,
            Content: bytes,
            Sha256: hash,
            SourceType: sourceType,
            SourcePath: sourceDirectory);
    }

    private static bool ShouldIncludeBundleFile(string sourceDirectory, string filePath)
    {
        var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');

        // Exclude Python/virtualenv artifacts
        if (normalized.Contains("/.venv/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".venv/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/__pycache__/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("__pycache__/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Exclude gitignore files
        if (Path.GetFileName(normalized).Equals(".gitignore", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Exclude root-level test scripts and README (not template content)
        var isRootFile = !normalized.Contains('/');
        if (isRootFile && (normalized.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ||
                           normalized.Equals("README.md", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static string? ResolveFixtureArtifactDirectory(string targetHireId, EmployeeDetailDto employee)
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            return null;
        }

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(targetHireId))
        {
            var normalizedHireId = targetHireId.Trim();
            candidates.Add(Path.Combine(fixtureRoot, normalizedHireId));
            candidates.Add(Path.Combine(fixtureRoot, normalizedHireId.Replace("hire_", "e_", StringComparison.OrdinalIgnoreCase)));
        }

        var templateHints = BuildTemplateHints(employee);
        foreach (var hint in templateHints.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            candidates.Add(Path.Combine(fixtureRoot, hint.Trim()));
            var binding = ResolveFixtureTemplateBinding(hint);
            if (!string.IsNullOrWhiteSpace(binding?.FixtureEmployeeId))
            {
                var fixtureEmployeeId = binding.FixtureEmployeeId!.Trim();
                candidates.Add(Path.Combine(fixtureRoot, fixtureEmployeeId));
                if (fixtureEmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(Path.Combine(fixtureRoot, $"hire_{fixtureEmployeeId[2..]}"));
                }
            }
        }

        return candidates
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(Path.GetFullPath)
            .FirstOrDefault(path =>
                Directory.Exists(path) &&
                (File.Exists(Path.Combine(path, "instance.json")) || Directory.Exists(Path.Combine(path, "testcases"))));
    }

}
