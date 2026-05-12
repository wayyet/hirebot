using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
        string owner,
        string employeeId,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var latestIteration = await dbContext.EvaluationSessions
            .AsNoTracking()
            .Where(item =>
                item.OwnerSubject == owner &&
                item.EmployeeId == employeeId)
            .Select(item => (int?)item.Iteration)
            .MaxAsync(cancellationToken) ?? 0;

        var session = new EvaluationSessionEntity
        {
            Id = Guid.NewGuid(),
            SessionId = BuildEvaluationSessionId(),
            OwnerSubject = owner,
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
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var latestSession = await dbContext.EvaluationSessions
            .Where(item =>
                item.OwnerSubject == owner &&
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
                OwnerSubject = owner,
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
        var stored = await evaluationAssetStore.SaveTextAsync(
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
        var stored = await evaluationAssetStore.SaveBytesAsync(
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

    private async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesAsync(
        EvaluationWorkspaceContext workspaceContext,
        EmployeeDetailDto employee,
        CancellationToken cancellationToken)
    {
        var fromTarget = await LoadTestcaseSourcesFromTargetArtifactsAsync(workspaceContext.TargetHireId, cancellationToken);
        if (fromTarget.Count > 0)
        {
            return fromTarget;
        }

        var templateHints = BuildTemplateHints(employee);

        // Also include the original employee ID so fixture lookup can find
        // the employee's own fixture directory (e.g. hire_dev_seed_401_asset-guardian).
        var hintsWithEmployeeId = new List<string>(templateHints) { employee.EmployeeId };
        if (employee.EmployeeId.StartsWith("e_", StringComparison.OrdinalIgnoreCase))
            hintsWithEmployeeId.Add($"hire_{employee.EmployeeId[2..]}");

        return await LoadTestcaseSourcesFromFixtureAsync(
            workspaceContext.TargetHireId,
            hintsWithEmployeeId,
            cancellationToken);
    }

    private async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromTargetArtifactsAsync(
        string targetHireId,
        CancellationToken cancellationToken)
    {
        var packageSnapshot = await artifactPackageService.GetLatestPackageAsync(targetHireId, cancellationToken);
        if (packageSnapshot?.Content is not { Length: > 0 })
        {
            return [];
        }

        var sources = new List<TestcaseSourceFile>();
        try
        {
            using var stream = new MemoryStream(packageSnapshot.Content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.Name) || !entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                var json = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                var normalizedPath = entry.FullName.Replace('\\', '/');
                var isTestcaseFolderEntry = normalizedPath.StartsWith("testcases/", StringComparison.OrdinalIgnoreCase);
                if (!isTestcaseFolderEntry &&
                    !json.Contains("test_case", StringComparison.OrdinalIgnoreCase) &&
                    !json.Contains("test_cases", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sources.Add(new TestcaseSourceFile(
                    FileName: Path.GetFileName(normalizedPath),
                    SourcePath: normalizedPath,
                    RawJson: json,
                    SourceType: packageSnapshot.Kind));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract target artifact testcase files. TargetHireId={TargetHireId}", targetHireId);
        }

        return sources;
    }

    private static async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromFixtureAsync(
        string targetHireId,
        IReadOnlyList<string> templateHints,
        CancellationToken cancellationToken)
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            return [];
        }

        // Prefer testcase bundle colocated with the target fixture package.
        var scopedRoot = ResolveScopedFixtureTestcaseRoot(fixtureRoot, targetHireId);
        var scopedSources = await LoadTestcaseSourcesFromDirectoryAsync(scopedRoot, "fixture-scoped", cancellationToken);
        if (scopedSources.Count > 0)
        {
            return scopedSources;
        }

        var templateScopedRoots = ResolveTemplateScopedFixtureTestcaseRoots(fixtureRoot, templateHints);
        foreach (var templateScopedRoot in templateScopedRoots)
        {
            var templateScopedSources = await LoadTestcaseSourcesFromDirectoryAsync(
                templateScopedRoot,
                "fixture-template-scoped",
                cancellationToken);
            if (templateScopedSources.Count > 0)
            {
                return templateScopedSources;
            }
        }

        return [];
    }

    private static async Task<IReadOnlyList<TestcaseSourceFile>> LoadTestcaseSourcesFromDirectoryAsync(
        string? sourceDirectory,
        string sourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return [];
        }

        var files = Directory.GetFiles(sourceDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            return [];
        }

        var sources = new List<TestcaseSourceFile>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            sources.Add(new TestcaseSourceFile(
                FileName: Path.GetFileName(file),
                SourcePath: file,
                RawJson: content,
                SourceType: sourceType));
        }

        return sources;
    }

    private async Task<ApiResponse<TargetArtifactWarmupResult>> EnsureTargetArtifactBundleLoadedAsync(
        string owner,
        EmployeeDetailDto employee,
        EvaluationWorkspaceContext workspaceContext,
        EvaluationSessionEntity sessionEntity,
        bool forceRefresh,
        string? explicitArtifactPath,
        CancellationToken cancellationToken)
    {
        var warmupKey = $"{BuildWorkspaceKey(owner, employee.EmployeeId)}::{workspaceContext.TargetHireId}";
        if (!forceRefresh && TargetArtifactPrimed.ContainsKey(warmupKey))
        {
            return ApiResponse<TargetArtifactWarmupResult>.SuccessResponse(new TargetArtifactWarmupResult(
                WorkspacePath: "hiring-conversation",
                SourceArtifactPath: "already-primed"));
        }

        var bundleResult = await BuildTargetArtifactBundleAsync(
            workspaceContext.TargetHireId,
            employee,
            explicitArtifactPath,
            cancellationToken);
        if (!bundleResult.Success || bundleResult.Data is null)
        {
            return ApiResponse<TargetArtifactWarmupResult>.ErrorResponse(bundleResult.Code, bundleResult.Message);
        }

        var bundle = bundleResult.Data;
        var zipAsset = await PersistBinaryAssetAsync(
            sessionEntity,
            assetType: "target-artifact-zip",
            relatedKey: $"target-artifact:{workspaceContext.TargetHireId}",
            fileName: bundle.FileName,
            content: bundle.Content,
            mimeType: "application/zip",
            sourceType: bundle.SourceType,
            cancellationToken);

        var startConversationResult = await EnsureSandboxConversationStartedAsync(
            employee.OwnerUserId,
            workspaceContext.TargetHireId,
            workspaceContext.TargetSandboxId,
            "evaluation-target",
            cancellationToken);
        if (!startConversationResult.Success && startConversationResult.Code != 409)
        {
            logger.LogInformation(
                "Target conversation start for artifact warmup skipped. TargetHireId={TargetHireId}, Code={Code}, Message={Message}",
                workspaceContext.TargetHireId,
                startConversationResult.Code,
                startConversationResult.Message);
        }

        var zipBase64 = Convert.ToBase64String(bundle.Content);
        var warmupMessage = BuildTargetArtifactWarmupPrompt(bundle.FileName, zipAsset.PublicUrl);
        var warmupSendResult = await SendSandboxMessageAsync(
            employee.OwnerUserId,
            workspaceContext.TargetHireId,
            workspaceContext.TargetSandboxId,
            "evaluation-target",
            new HiringConversationMessageRequestDto
            {
                Content = warmupMessage,
                StructuredAnswers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["artifact_bundle_name"] = bundle.FileName,
                    ["artifact_bundle_sha256"] = bundle.Sha256,
                    ["artifact_bundle_public_url"] = zipAsset.PublicUrl,
                    ["artifact_bundle_source"] = bundle.SourceType
                },
                Materials =
                [
                    new HiringConversationMaterialDto
                    {
                        Type = "file",
                        Name = bundle.FileName,
                        Content = zipBase64,
                        ContentHash = bundle.Sha256,
                        Size = bundle.Content.LongLength,
                        MimeType = "application/zip",
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["encoding"] = "base64",
                            ["source_type"] = bundle.SourceType,
                            ["source_path"] = bundle.SourcePath
                        }
                    }
                ]
            },
            cancellationToken);

        if (!warmupSendResult.Success)
        {
            return ApiResponse<TargetArtifactWarmupResult>.ErrorResponse(
                warmupSendResult.Code,
                $"failed to send target artifact attachment: {warmupSendResult.Message}");
        }

        TargetArtifactPrimed[warmupKey] = 0;
        await UpdateSessionStatusAsync(sessionEntity, "target_artifact_primed", null, cancellationToken);

        return ApiResponse<TargetArtifactWarmupResult>.SuccessResponse(new TargetArtifactWarmupResult(
            WorkspacePath: "hiring-conversation",
            SourceArtifactPath: bundle.SourcePath));
    }

    private async Task<ApiResponse<TargetArtifactBundle>> BuildTargetArtifactBundleAsync(
        string targetHireId,
        EmployeeDetailDto employee,
        string? explicitArtifactPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitArtifactPath))
        {
            var normalizedPath = explicitArtifactPath.Trim();
            if (Directory.Exists(normalizedPath))
            {
                var explicitZip = await ZipDirectoryAsBundleAsync(
                    normalizedPath,
                    $"{Path.GetFileName(normalizedPath)}.zip",
                    sourceType: "explicit-directory",
                    cancellationToken);
                return ApiResponse<TargetArtifactBundle>.SuccessResponse(explicitZip);
            }

            if (File.Exists(normalizedPath) &&
                normalizedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await File.ReadAllBytesAsync(normalizedPath, cancellationToken);
                if (bytes.Length > 0)
                {
                    var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    return ApiResponse<TargetArtifactBundle>.SuccessResponse(new TargetArtifactBundle(
                        FileName: Path.GetFileName(normalizedPath),
                        Content: bytes,
                        Sha256: hash,
                        SourceType: "explicit-zip",
                        SourcePath: normalizedPath));
                }
            }

            return ApiResponse<TargetArtifactBundle>.ErrorResponse(404, $"explicit artifact path not found: {normalizedPath}");
        }

        var packageSnapshot = await artifactPackageService.GetLatestPackageAsync(targetHireId, cancellationToken);
        if (packageSnapshot?.Content is { Length: > 0 })
        {
            var sourceName = string.IsNullOrWhiteSpace(packageSnapshot.FileName)
                ? $"hiring_artifacts_{targetHireId}.zip"
                : packageSnapshot.FileName;
            var hash = Convert.ToHexStringLower(SHA256.HashData(packageSnapshot.Content));
            return ApiResponse<TargetArtifactBundle>.SuccessResponse(new TargetArtifactBundle(
                FileName: sourceName,
                Content: packageSnapshot.Content,
                Sha256: hash,
                SourceType: packageSnapshot.Kind,
                SourcePath: targetHireId));
        }

        return ApiResponse<TargetArtifactBundle>.ErrorResponse(404, "target artifact package not found");
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

    private static string BuildTargetArtifactWarmupPrompt(string fileName, string publicUrl)
    {
        return $"""
                [ArtifactWarmup]
                你将收到一个压缩包附件：{fileName}
                请先解压并完整学习其中的全部资料（config/skills/ontology/testcases 等），再执行后续测试场景。
                附件的资源链接（如需校验）：{publicUrl}
                学习完成后请回复：READY_FOR_EVALUATION
                """;
    }

}
