using System.Collections.Concurrent;
using System.IO.Compression;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Hiring.Artifacts;
using HireBot.Core.Services.Hiring.Discovery;
using HireBot.Core.Services.Hiring.Storage;
using HireBot.Core.Services.Hiring.TemplatePackages;
using HireBot.Core.Services.EmployeeRuntime;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class EmployeeHiringService
{
    private static readonly JsonSerializerOptions ExternalPackageJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private static readonly string[] ManagedExternalPackagePaths =
    [
        "external/user-config.json",
        "external/external-config.index.json",
        "external/systems/cli.json",
        "external/systems/mcp.json",
        "external/README.md"
    ];

    private static HiringStagePreviewDto BuildLocalStagePreview(
        string hireId,
        DiscoverySkillDefinition discoverySkill,
        IReadOnlyList<HiringStageCompletionDto> stageCompletion,
        string currentStage,
        string collectionPhase,
        IReadOnlyDictionary<string, string?> structuredData,
        string? summaryOverride)
    {
        var basePreview = new HiringStagePreviewDto(
            HireId: hireId,
            Stage: currentStage,
            SkillName: string.Empty,
            Summary: string.IsNullOrWhiteSpace(summaryOverride) ? $"当前阶段：{currentStage}" : summaryOverride.Trim(),
            StructuredData: structuredData,
            MissingFields: [],
            RiskNotes: [],
            ReadyForAudit: false,
            GeneratedAt: DateTimeOffset.UtcNow);

        return EnrichStagePreview(
            basePreview,
            discoverySkill,
            stageCompletion,
            currentStage,
            collectionPhase,
            structuredData);
    }

    internal static byte[] BuildDigitalEmployeeArchive(
        TemplatePackageDefinition templatePackage)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in templatePackage.PackageFiles)
            {
                if (file.Content.Length == 0 || string.IsNullOrWhiteSpace(file.RelativePath))
                {
                    continue;
                }

                if (!TryNormalizeArchiveEntryPath(file.RelativePath, out var normalizedPath))
                {
                    continue;
                }

                var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(file.Content, 0, file.Content.Length);
            }
        }

        return memoryStream.ToArray();
    }

    private static IReadOnlyList<HiringConversationMaterialDto> BuildMaterialsFromRequest(HiringConversationMessageRequestDto request)
    {
        var result = new List<HiringConversationMaterialDto>();
        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            var content = request.Content.Trim();
            result.Add(new HiringConversationMaterialDto
            {
                Type = "text",
                Name = $"conversation-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
                Content = content,
                ContentHash = ComputeContentHash(content),
                Size = Encoding.UTF8.GetByteCount(content),
                MimeType = "text/plain",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source"] = "conversation"
                }
            });
        }

        if (request.Materials is not null)
        {
            foreach (var material in request.Materials)
            {
                var normalized = NormalizeMaterial(material);
                if (normalized is not null)
                {
                    result.Add(normalized);
                }
            }
        }

        return result;
    }

    internal static HiringRuntimeContext ApplyConversationProgressToTemplatePackage(HiringRuntimeContext runtimeContext)
    {
        var enrichedFiles = runtimeContext.WorkingTemplatePackage.PackageFiles.ToDictionary(
            file => file.RelativePath,
            file => file,
            StringComparer.OrdinalIgnoreCase);

        var structuredDataJson = JsonSerializer.Serialize(runtimeContext.StructuredData, JsonOptions);
        var materialsJson = JsonSerializer.Serialize(runtimeContext.Materials, JsonOptions);
        UpsertPackageFile(enrichedFiles, "ontology/hiring-session/structured-data.json", structuredDataJson);
        UpsertPackageFile(enrichedFiles, "ontology/hiring-session/materials.json", materialsJson);
        if (!runtimeContext.PackagingTestCasesStaged &&
            TryBuildEvaluationTestCases(runtimeContext, out var evaluationTestCasesJson))
        {
            UpsertPackageFile(enrichedFiles, "testcases/evaluation-test-cases.json", evaluationTestCasesJson);
            UpsertPackageFile(enrichedFiles, "ontology/hiring-session/evaluation-test-cases.json", evaluationTestCasesJson);
        }

        SyncManagedExternalPackageFiles(enrichedFiles, runtimeContext.ExternalSystemConfig);

        var enrichedTemplatePackage = runtimeContext.WorkingTemplatePackage with
        {
            PackageFiles = enrichedFiles.Values.ToArray()
        };

        return runtimeContext with
        {
            WorkingTemplatePackage = enrichedTemplatePackage
        };
    }

    internal static void UpsertPackageFile(
        IDictionary<string, TemplatePackageFileAsset> packageFiles,
        string relativePath,
        string content)
    {
        var normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        packageFiles[normalizedPath] = new TemplatePackageFileAsset(normalizedPath, bytes, hash);
    }

    internal static void OverlayManagedExternalPackageArtifacts(
        IDictionary<string, byte[]> artifacts,
        HiringExternalSystemConfigState? externalSystemConfig)
    {
        RemoveManagedExternalPackageFiles(artifacts);
        foreach (var (relativePath, content) in BuildManagedExternalPackageFiles(externalSystemConfig))
        {
            artifacts[relativePath] = Encoding.UTF8.GetBytes(content);
        }
    }

    private static void SyncManagedExternalPackageFiles(
        IDictionary<string, TemplatePackageFileAsset> packageFiles,
        HiringExternalSystemConfigState? externalSystemConfig)
    {
        RemoveManagedExternalPackageFiles(packageFiles);
        foreach (var (relativePath, content) in BuildManagedExternalPackageFiles(externalSystemConfig))
        {
            UpsertPackageFile(packageFiles, relativePath, content);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildManagedExternalPackageFiles(
        HiringExternalSystemConfigState? externalSystemConfig)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (externalSystemConfig is null || !externalSystemConfig.HasAnyConfig)
        {
            return files;
        }

        var configDto = externalSystemConfig.ToDto();
        var cliTools = configDto.CliTools
            .Select(tool => new
            {
                tool.ToolName,
                tool.Description,
                tool.ExecutionMode,
                ArgumentTemplate = string.IsNullOrWhiteSpace(tool.ArgumentTemplate) ? null : tool.ArgumentTemplate
            })
            .ToArray();

        var mcpServer = configDto.McpServer;
        var snapshot = new
        {
            schemaVersion = "1.0.0",
            artifactType = "external_user_config",
            source = "manual_form",
            updatedAtUtc = configDto.UpdatedAtUtc ?? externalSystemConfig.UpdatedAtUtc,
            cliTools,
            mcpServer = mcpServer is null
                ? null
                : new
                {
                    mcpServer.ServerUrl,
                    Auth = new
                    {
                        Kind = mcpServer.AuthMode,
                        HasBoundSecret = mcpServer.HasApiKey,
                        CredentialSlot = mcpServer.HasApiKey ? "mcp_api_key" : null
                    },
                    SelectedTools = mcpServer.SelectedTools
                }
        };
        files["external/user-config.json"] = JsonSerializer.Serialize(snapshot, ExternalPackageJsonOptions);

        var systems = new List<object>();
        var fileEntries = new List<object>
        {
            new
            {
                path = "external/user-config.json",
                kind = "snapshot"
            }
        };

        if (cliTools.Length > 0)
        {
            var cliSystem = new
            {
                schemaVersion = "1.0.0",
                artifactType = "external_system",
                systemKey = "cli",
                displayName = "CLI",
                integrationMethod = "cli",
                toolCount = cliTools.Length,
                tools = cliTools,
                configPath = "external/user-config.json"
            };
            files["external/systems/cli.json"] = JsonSerializer.Serialize(cliSystem, ExternalPackageJsonOptions);
            systems.Add(new
            {
                systemKey = "cli",
                displayName = "CLI",
                path = "external/systems/cli.json"
            });
            fileEntries.Add(new
            {
                path = "external/systems/cli.json",
                kind = "system"
            });
        }

        if (mcpServer is not null && (!string.IsNullOrWhiteSpace(mcpServer.ServerUrl) || mcpServer.SelectedTools.Count > 0))
        {
            var mcpSystem = new
            {
                schemaVersion = "1.0.0",
                artifactType = "external_system",
                systemKey = "mcp",
                displayName = "MCP",
                integrationMethod = "mcp",
                mcpServer.ServerUrl,
                Auth = new
                {
                    Kind = mcpServer.AuthMode,
                    HasBoundSecret = mcpServer.HasApiKey,
                    CredentialSlot = mcpServer.HasApiKey ? "mcp_api_key" : null
                },
                SelectedTools = mcpServer.SelectedTools,
                configPath = "external/user-config.json"
            };
            files["external/systems/mcp.json"] = JsonSerializer.Serialize(mcpSystem, ExternalPackageJsonOptions);
            systems.Add(new
            {
                systemKey = "mcp",
                displayName = "MCP",
                path = "external/systems/mcp.json"
            });
            fileEntries.Add(new
            {
                path = "external/systems/mcp.json",
                kind = "system"
            });
        }

        var index = new
        {
            schemaVersion = "1.0.0",
            artifactType = "external_config_index",
            source = "manual_form",
            updatedAtUtc = configDto.UpdatedAtUtc ?? externalSystemConfig.UpdatedAtUtc,
            systems,
            files = fileEntries
        };
        files["external/external-config.index.json"] = JsonSerializer.Serialize(index, ExternalPackageJsonOptions);

        var readme = BuildManagedExternalReadme(configDto);
        files["external/README.md"] = readme;
        return files;
    }

    private static string BuildManagedExternalReadme(HiringExternalSystemConfigDto config)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# External Config");
        builder.AppendLine();
        builder.AppendLine("该目录由外部系统配置表单生成，供后续流程读取。");
        builder.AppendLine();

        if (config.CliTools.Count > 0)
        {
            builder.AppendLine("## CLI");
            foreach (var tool in config.CliTools)
            {
                builder.Append("- ");
                builder.Append(tool.ToolName);
                builder.Append("（");
                builder.Append(tool.ExecutionMode);
                builder.AppendLine("）");
            }

            builder.AppendLine();
        }

        if (config.McpServer is not null)
        {
            builder.AppendLine("## MCP");
            builder.AppendLine($"- Server: {config.McpServer.ServerUrl}");
            builder.AppendLine($"- Auth Mode: {config.McpServer.AuthMode}");
            if (config.McpServer.SelectedTools.Count > 0)
            {
                builder.AppendLine($"- Tools: {string.Join(", ", config.McpServer.SelectedTools)}");
            }

            if (config.McpServer.HasApiKey)
            {
                builder.AppendLine("- API Key: 已通过安全存储绑定，未写入模板包");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void RemoveManagedExternalPackageFiles<TValue>(IDictionary<string, TValue> packageFiles)
    {
        foreach (var managedPath in ManagedExternalPackagePaths)
        {
            packageFiles.Remove(managedPath);
        }
    }

    private static bool TryBuildEvaluationTestCases(HiringRuntimeContext runtimeContext, out string testCasesJson)
    {
        testCasesJson = string.Empty;
        var evaluationSkillMaterials = runtimeContext.Materials
            .Where(material => string.Equals(material.Type, "skill", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (evaluationSkillMaterials.Length == 0)
        {
            return false;
        }

        var guidanceLines = new List<string>();
        foreach (var material in evaluationSkillMaterials)
        {
            if (material.Metadata?.TryGetValue("skillName", out var skillName) == true && !string.IsNullOrWhiteSpace(skillName))
            {
                guidanceLines.Add($"skillName: {skillName.Trim()}");
            }

            if (material.Metadata?.TryGetValue("description", out var description) == true && !string.IsNullOrWhiteSpace(description))
            {
                guidanceLines.Add($"description: {description.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(material.Content))
            {
                var skillArchiveGuidance = ExtractEvaluationGuidanceFromArchive(material);
                if (!string.IsNullOrWhiteSpace(skillArchiveGuidance))
                {
                    guidanceLines.Add(skillArchiveGuidance);
                }
                else
                {
                    guidanceLines.Add(material.Content.Trim());
                }
            }
        }

        var skillGuidance = string.Join('\n', guidanceLines).Trim();

        var businessGoal = ResolveStructuredValue(runtimeContext.StructuredData, "business_goal", "expected_outcome", "goal")
                           ?? runtimeContext.TemplateName;
        var userProfile = ResolveStructuredValue(runtimeContext.StructuredData, "user_profile", "owner")
                          ?? "业务团队";
        var scenario = ResolveStructuredValue(runtimeContext.StructuredData, "expected_outcome", "trigger_event")
                       ?? "关键业务流程";

        var testCases = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            source = "conversation-skill-guided",
            skillSummary = Truncate(skillGuidance, 1200),
            cases = new object[]
            {
                new
                {
                    caseId = "eval-case-001",
                    title = $"{businessGoal} - 正常流程闭环",
                    objective = "验证数字员工在标准输入下能够完整执行流程并形成闭环回复",
                    profile = userProfile,
                    scenario,
                    expectedChecks = new[]
                    {
                        "覆盖预期行为序列的关键步骤",
                        "输出包含明确结论和下一步动作",
                        "关键字段采集完整且无空值"
                    }
                },
                new
                {
                    caseId = "eval-case-002",
                    title = $"{businessGoal} - 异常路径处置",
                    objective = "验证数字员工在信息缺失或异常输入下能够回退并给出风险提示",
                    profile = userProfile,
                    scenario = "输入缺失关键字段或存在冲突信息",
                    expectedChecks = new[]
                    {
                        "识别阻塞字段并明确追问",
                        "不跳过关键校验步骤",
                        "给出可执行的处置方案"
                    }
                },
                new
                {
                    caseId = "eval-case-003",
                    title = $"{businessGoal} - 工具调用与合规",
                    objective = "验证数字员工工具调用时机、参数和流程合规性",
                    profile = userProfile,
                    scenario,
                    expectedChecks = new[]
                    {
                        "必须工具调用不缺失",
                        "工具参数与上下文一致",
                        "流程顺序和合规约束满足要求"
                    }
                }
            }
        };

        testCasesJson = JsonSerializer.Serialize(testCases, JsonOptions);
        return true;
    }

    private static string? ExtractEvaluationGuidanceFromArchive(HiringConversationMaterialDto material)
    {
        var storagePath = material.Metadata is not null && material.Metadata.TryGetValue("storagePath", out var storagePathValue)
            ? storagePathValue
            : null;

        var archiveFormat = material.Metadata is not null && material.Metadata.TryGetValue("archiveFormat", out var archiveFormatValue)
            ? archiveFormatValue
            : null;
        var isZip = material.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(archiveFormat, "zip", StringComparison.OrdinalIgnoreCase);
        if (!isZip)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(material.Content))
        {
            return ExtractEvaluationGuidanceFromStoredArchive(storagePath);
        }

        var base64Content = material.Content.Trim();
        var base64Index = base64Content.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (base64Index >= 0)
        {
            base64Content = base64Content[(base64Index + "base64,".Length)..];
        }

        byte[] archiveBytes;
        try
        {
            archiveBytes = Convert.FromBase64String(base64Content);
        }
        catch (FormatException)
        {
            return null;
        }

        var snippets = new List<string>();
        using var memoryStream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (!entry.FullName.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            snippets.Add($"[{entry.FullName}]");
            snippets.Add(Truncate(content, 2000));
        }

        if (snippets.Count == 0)
        {
            return null;
        }

        return string.Join('\n', snippets);
    }

    private static string? ExtractEvaluationGuidanceFromStoredArchive(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || !File.Exists(storagePath))
        {
            return null;
        }

        var snippets = new List<string>();
        using var stream = File.OpenRead(storagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (!entry.FullName.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            snippets.Add($"[{entry.FullName}]");
            snippets.Add(Truncate(content, 2000));
        }

        return snippets.Count == 0 ? null : string.Join('\n', snippets);
    }

    private static string? ResolveStructuredValue(
        IReadOnlyDictionary<string, string?> structuredData,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (structuredData.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return $"{trimmed[..maxLength]}...";
    }

    private async Task PersistIntermediatePackageAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        await artifactPackageService.PersistIntermediatePackageAsync(
            new HiringArtifactPackagePersistRequestDto(
                runtimeContext.HireId,
                runtimeContext.SessionId,
                BuildIntermediatePackageFileName(runtimeContext.HireId),
                BuildPackageFileMap(runtimeContext.WorkingTemplatePackage)),
            cancellationToken);
    }

    private static IReadOnlyDictionary<string, byte[]> BuildPackageFileMap(TemplatePackageDefinition templatePackage)
    {
        return templatePackage.PackageFiles.ToDictionary(
            file => file.RelativePath,
            file => file.Content,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool ShouldPersistArtifactPackages(HiringRuntimeContext runtimeContext)
    {
        return !string.IsNullOrWhiteSpace(runtimeContext.SessionId) &&
               !string.Equals(
                   runtimeContext.TemplateId,
                   EvaluationWorkspaceTemplateId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildIntermediatePackageFileName(string hireId)
    {
        return $"{hireId.Trim()}_intermediate_package.zip";
    }

    private static string BuildFinalPackageFileName(string hireId, string? upstreamFileName)
    {
        return string.IsNullOrWhiteSpace(upstreamFileName)
            ? $"{hireId.Trim()}_final_package.zip"
            : upstreamFileName.Trim();
    }

    private static HiringConversationMaterialDto? NormalizeMaterial(HiringConversationMaterialDto? material)
    {
        if (material is null)
        {
            return null;
        }

        var type = string.IsNullOrWhiteSpace(material.Type) ? "file" : material.Type.Trim();
        var name = string.IsNullOrWhiteSpace(material.Name)
            ? $"{type}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}"
            : material.Name.Trim();
        var content = string.IsNullOrWhiteSpace(material.Content) ? null : material.Content;
        return material with
        {
            Type = type,
            Name = name,
            Content = content,
            ContentHash = string.IsNullOrWhiteSpace(material.ContentHash) && content is not null
                ? ComputeContentHash(content)
                : material.ContentHash,
            Size = material.Size ?? (content is null ? null : Encoding.UTF8.GetByteCount(content)),
            MimeType = string.IsNullOrWhiteSpace(material.MimeType) ? null : material.MimeType.Trim(),
            Metadata = material.Metadata?
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<HiringConversationMaterialDto> MergeMaterials(
        IReadOnlyList<HiringConversationMaterialDto> existing,
        IReadOnlyList<HiringConversationMaterialDto> incoming)
    {
        if (incoming.Count == 0)
        {
            return existing;
        }

        var result = existing.ToList();
        foreach (var material in incoming)
        {
            var hasDuplicate = !string.IsNullOrWhiteSpace(material.ContentHash) &&
                               result.Any(existingMaterial => string.Equals(
                                   existingMaterial.ContentHash,
                                   material.ContentHash,
                                   StringComparison.OrdinalIgnoreCase));
            if (!hasDuplicate)
            {
                result.Add(material);
            }
        }

        return result;
    }

    private static string ComputeContentHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static Dictionary<string, string?> NormalizeStructuredData(IReadOnlyDictionary<string, string?>? source)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            result[pair.Key.Trim()] = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
        }

        return result;
    }

    private static Dictionary<string, string?> MergeStructuredData(
        IReadOnlyDictionary<string, string?> existing,
        IReadOnlyDictionary<string, string>? incoming)
    {
        var result = NormalizeStructuredData(existing);
        if (incoming is null)
        {
            return result;
        }

        foreach (var pair in incoming)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            result[pair.Key.Trim()] = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
        }

        return result;
    }

    private async Task<RemoteCallResult<T>> SendForJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var call = await kingCrabHttpClient.SendForJsonAsync<T>(
            method,
            path,
            body,
            ownerSubject,
            cancellationToken);

        return call.Success && call.Data is not null
            ? RemoteCallResult<T>.Ok(call.Data)
            : RemoteCallResult<T>.Failure(call.StatusCode, call.Message);
    }

    private async Task<RemoteCallResult<T>> SendMultipartForJsonAsync<T>(
        string path,
        string formFieldName,
        string fileName,
        byte[] fileBytes,
        string contentType,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var call = await kingCrabHttpClient.SendMultipartForJsonAsync<T>(
            path,
            formFieldName,
            fileName,
            fileBytes,
            contentType,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false);

        return call.Success && call.Data is not null
            ? RemoteCallResult<T>.Ok(call.Data)
            : RemoteCallResult<T>.Failure(call.StatusCode, call.Message);
    }

    /// <summary>
    /// 解压沙箱产物 zip 为相对路径到字节的字典；同时执行三类兜底清洗：
    /// 1) 剥离所有 entry 共同的顶层目录（沙箱 package_workspace 可能多包一层 workspace 目录）；
    /// 2) 过滤 uploads/ 临时区——原始模板包通过 WorkingTemplatePackage 单独合并，不应再次塞回产物；
    /// 3) 过滤隐藏目录/文件（"."开头）以及常见临时文件后缀。
    /// </summary>
    private static IReadOnlyDictionary<string, byte[]> ExtractZipEntries(byte[] archiveBytes)
    {
        using var memoryStream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read, leaveOpen: false);

        // 第一遍：收集所有有效 entry 的规范化路径
        var rawEntries = new List<(string Path, ZipArchiveEntry Entry)>();
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (!TryNormalizeArchiveEntryPath(entry.FullName, out var normalizedPath))
            {
                continue;
            }

            rawEntries.Add((normalizedPath, entry));
        }

        // 第二遍：探测共同顶层目录前缀（仅当所有 entry 都以同一段开头时才剥离，避免误伤）
        var commonRoot = DetectCommonTopLevelDirectory(rawEntries.Select(item => item.Path).ToList());

        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, entry) in rawEntries)
        {
            var stripped = !string.IsNullOrEmpty(commonRoot) && path.StartsWith(commonRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? path[(commonRoot.Length + 1)..]
                : path;

            if (string.IsNullOrWhiteSpace(stripped) || ShouldExcludeArtifactPath(stripped))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            result[stripped] = buffer.ToArray();
        }

        return result;
    }

    /// <summary>
    /// 检测所有 entry 是否共享同一个顶层目录；若是则返回该目录名，否则返回 null。
    /// 用于剥离沙箱 package_workspace 可能多包的一层 workspace 同名目录。
    /// </summary>
    private static string? DetectCommonTopLevelDirectory(IReadOnlyList<string> normalizedPaths)
    {
        if (normalizedPaths.Count == 0)
        {
            return null;
        }

        string? candidate = null;
        foreach (var path in normalizedPaths)
        {
            var slashIndex = path.IndexOf('/');
            if (slashIndex <= 0)
            {
                // 存在根级文件（如 manifest.json）则不剥离
                return null;
            }

            var head = path[..slashIndex];
            if (candidate is null)
            {
                candidate = head;
                continue;
            }

            if (!string.Equals(candidate, head, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        // 白名单顶层目录不剥离（例如所有 entry 都恰好在 skills/ 下）
        if (candidate is not null && ArtifactRootWhitelist.Contains(candidate))
        {
            return null;
        }

        return candidate;
    }

    /// <summary>
    /// 黑名单：以 uploads/ 起始的临时区、隐藏目录/文件、常见临时文件后缀均跳过。
    /// </summary>
    private static bool ShouldExcludeArtifactPath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return true;
        }

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.StartsWith('.'))
            {
                return true; // 任何 .开头隐藏段（.git/.cache/.DS_Store/.venv 等）
            }
        }

        // uploads/ 是沙箱临时输入区，绝不允许混入最终产物
        if (normalizedPath.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedPath, "uploads", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 临时文件后缀
        var lastSegment = segments.Length > 0 ? segments[^1] : normalizedPath;
        if (lastSegment.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            lastSegment.EndsWith(".swp", StringComparison.OrdinalIgnoreCase) ||
            lastSegment.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(lastSegment, "Thumbs.db", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static readonly HashSet<string> ArtifactRootWhitelist =
        new(StringComparer.OrdinalIgnoreCase) { "manifest", "ontology", "skills", "external", "config", "testcases" };

    /// <summary>
    /// 按优先级合并三层产物：沙箱生成产物（最高） &gt; store skill 关联包（中层） &gt; 原始模板包（最低）。
    /// 后写入者不能覆盖已存在键，从而保证用户在沙箱里的最终编辑、和用户主动选择的 store skill 都不会被旧模板回写。
    /// </summary>
    private static Dictionary<string, byte[]> MergeTemplatePackageArtifacts(
        IReadOnlyDictionary<string, byte[]> generatedArtifacts,
        IReadOnlyDictionary<string, byte[]> storeSkillArtifacts,
        TemplatePackageDefinition templatePackage)
    {
        var mergedArtifacts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in generatedArtifacts)
        {
            if (!TryNormalizeArchiveEntryPath(pair.Key, out var normalizedPath) || pair.Value.Length == 0)
            {
                continue;
            }

            mergedArtifacts[normalizedPath] = pair.Value;
        }

        foreach (var pair in storeSkillArtifacts)
        {
            if (!TryNormalizeArchiveEntryPath(pair.Key, out var normalizedPath) || pair.Value.Length == 0)
            {
                continue;
            }

            // 沙箱已有则尊重沙箱，否则补上 store skill 文件
            mergedArtifacts.TryAdd(normalizedPath, pair.Value);
        }

        foreach (var packageFile in templatePackage.PackageFiles)
        {
            if (!TryNormalizeArchiveEntryPath(packageFile.RelativePath, out var normalizedPath) ||
                packageFile.Content.Length == 0)
            {
                continue;
            }

            mergedArtifacts.TryAdd(normalizedPath, packageFile.Content);
        }

        return mergedArtifacts;
    }

    private static byte[] BuildArtifactArchive(IReadOnlyDictionary<string, byte[]> files)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!TryNormalizeArchiveEntryPath(pair.Key, out var normalizedPath) || pair.Value.Length == 0)
                {
                    continue;
                }

                var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(pair.Value, 0, pair.Value.Length);
            }
        }

        return memoryStream.ToArray();
    }

    private static bool TryNormalizeArchiveEntryPath(string path, out string normalizedPath)
    {
        return TryNormalizeArtifactPath(path, out normalizedPath, out _);
    }

}
