using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HireBot.Core.Services.Hiring.Artifacts;

internal sealed class PlaceholderArtifactSerializer : IArtifactSerializer
{
    private const string OntologyArtifactFileName = "ontology-slice.contract.json";
    private const string SkillArtifactFileName = "business-skill-package.contract.json";
    private const string StructuredPackageRoot = "template-package";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ArtifactSerializationResult Serialize(ArtifactSerializationRequest request)
    {
        var ontologyArtifact = BuildOntologyArtifact(request);
        var skillArtifact = BuildSkillArtifact(request);

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [OntologyArtifactFileName] = JsonSerializer.SerializeToUtf8Bytes(ontologyArtifact, JsonOptions),
            [SkillArtifactFileName] = JsonSerializer.SerializeToUtf8Bytes(skillArtifact, JsonOptions)
        };

        var archiveBytes = BuildArchive(files, request);
        return new ArtifactSerializationResult(
            Files: files,
            Archive: archiveBytes,
            ArchiveFileName: $"{request.HireId}_artifacts.zip");
    }

    private static object BuildOntologyArtifact(ArtifactSerializationRequest request)
    {
        return new
        {
            contractVersion = "v1-structure-aligned",
            artifactType = "ontology-slice-package",
            generatedAt = request.GeneratedAtUtc,
            hire = new
            {
                request.HireId,
                request.TemplateId,
                request.TemplateName,
                request.EmployeeId,
                request.OwnerSubject,
                request.TenantId,
                request.OperatorId,
                request.SandboxId,
                request.SessionId,
                request.CurrentStage,
                request.CollectionPhase
            },
            templatePackage = new
            {
                request.TemplatePackage.PackageId,
                request.TemplatePackage.PackageVersion,
                request.TemplatePackage.PackageHash,
                manifest = ParseJsonOrText(request.TemplatePackage.ManifestJson),
                sourceFilePaths = request.TemplatePackage.OntologySlices
                    .Select(slice => slice.RelativePath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                files = request.TemplatePackage.OntologySlices.Select(slice =>
                {
                    var enrichedContent = BuildOptimizedContent(slice.Content, request);
                    return new
                    {
                        slice.Name,
                        path = slice.RelativePath,
                        slice.Type,
                        slice.Required,
                        sourceContentHash = slice.ContentHash,
                        enrichedContentHash = ComputeContentHash(enrichedContent),
                        enrichedContent
                    };
                })
            },
            discoverySkill = new
            {
                request.DiscoverySkill.SkillId,
                request.DiscoverySkill.SkillVersion,
                request.DiscoverySkill.SkillHash
            },
            x_ncrew_enrichment = new
            {
                sourceMaterials = BuildMaterialSummaries(request),
                goal = new
                {
                    businessGoal = GetValue(request.StructuredData, "business_goal"),
                    owner = GetValue(request.StructuredData, "owner"),
                    successMetric = GetValue(request.StructuredData, "success_metric")
                },
                scenario = new
                {
                    userProfile = GetValue(request.StructuredData, "user_profile"),
                    triggerEvent = GetValue(request.StructuredData, "trigger_event"),
                    expectedOutcome = GetValue(request.StructuredData, "expected_outcome")
                },
                systems = new
                {
                    systemList = GetValue(request.StructuredData, "system_list"),
                    permissionScope = GetValue(request.StructuredData, "permission_scope"),
                    dataSources = GetValue(request.StructuredData, "data_sources")
                },
                risks = new
                {
                    blockers = GetValue(request.StructuredData, "blockers"),
                    riskLevel = GetValue(request.StructuredData, "risk_level"),
                    fallbackPlan = GetValue(request.StructuredData, "fallback_plan")
                }
            }
        };
    }

    private static object BuildSkillArtifact(ArtifactSerializationRequest request)
    {
        return new
        {
            contractVersion = "v1-structure-aligned",
            artifactType = "business-skill-package",
            generatedAt = request.GeneratedAtUtc,
            hire = new
            {
                request.HireId,
                request.TemplateId,
                request.TemplateName,
                request.EmployeeId
            },
            templatePackage = new
            {
                request.TemplatePackage.PackageId,
                request.TemplatePackage.PackageVersion,
                request.TemplatePackage.PackageHash,
                manifest = ParseJsonOrText(request.TemplatePackage.ManifestJson),
                sourceFilePaths = request.TemplatePackage.RequiredSkills
                    .Select(skill => skill.RelativePath)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                files = request.TemplatePackage.RequiredSkills.Select(skill =>
                {
                    var enrichedContent = BuildOptimizedContent(skill.Content, request);
                    return new
                    {
                        skill.Name,
                        path = skill.RelativePath,
                        skill.Required,
                        sourceContentHash = skill.ContentHash,
                        enrichedContentHash = ComputeContentHash(enrichedContent),
                        enrichedContent
                    };
                })
            },
            discoverySkill = new
            {
                request.DiscoverySkill.SkillId,
                request.DiscoverySkill.SkillVersion,
                request.DiscoverySkill.SkillHash,
                files = request.DiscoverySkill.Files.Select(file => new
                {
                    file.RelativePath,
                    file.ContentHash,
                    file.Content
                }),
                stages = request.DiscoverySkill.StageRules.Select(rule => new
                {
                    rule.Stage,
                    rule.SkillName,
                    rule.Description,
                    rule.RequiredFields
                })
            },
            x_ncrew_enrichment = new
            {
                sourceMaterials = BuildMaterialSummaries(request),
                optimizationBasis = BuildMaterialBasis(request),
                package = new
                {
                    runbook = GetValue(request.StructuredData, "runbook"),
                    acceptanceCriteria = GetValue(request.StructuredData, "acceptance_criteria"),
                    deliveryWindow = GetValue(request.StructuredData, "delivery_window")
                },
                completion = request.StageCompletion.Select(item => new
                {
                    item.Stage,
                    item.RequiredFieldCount,
                    item.SatisfiedFieldCount,
                    item.CompletionRate,
                    item.SatisfiedFields,
                    item.BlockingFields,
                    item.ReadyForNextStage
                })
            }
        };
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> structuredData, string fieldName)
    {
        return structuredData.TryGetValue(fieldName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static IReadOnlyList<object> BuildMaterialSummaries(ArtifactSerializationRequest request)
    {
        return request.Materials
            .Select(material => new
            {
                material.Type,
                material.Name,
                material.Size,
                material.MimeType,
                material.ContentHash,
                contentPreview = Preview(material.Content),
                material.Metadata
            })
            .Cast<object>()
            .ToArray();
    }

    private static object BuildMaterialBasis(ArtifactSerializationRequest request)
    {
        return new
        {
            ruleSource = request.DiscoverySkill.SkillId,
            request.DiscoverySkill.SkillVersion,
            request.DiscoverySkill.SkillHash,
            materialCount = request.Materials.Count,
            structuredFieldCount = request.StructuredData.Count,
            note = "Structure-aligned V1: keep template package paths unchanged and enrich content with conversation/files/skill materials."
        };
    }

    private static string BuildOptimizedContent(string sourceContent, ArtifactSerializationRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine(sourceContent);
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine("x-ncrew-v1-optimization:");
        builder.AppendLine($"  discoverySkill: {request.DiscoverySkill.SkillId}@{request.DiscoverySkill.SkillVersion}");
        builder.AppendLine($"  materialCount: {request.Materials.Count}");
        builder.AppendLine($"  structuredFieldCount: {request.StructuredData.Count}");

        foreach (var pair in request.StructuredData.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                builder.AppendLine($"  {pair.Key}: {pair.Value}");
            }
        }

        if (request.Materials.Count > 0)
        {
            builder.AppendLine("  materials:");
            foreach (var material in request.Materials.Take(20))
            {
                builder.AppendLine($"    - [{material.Type}] {material.Name}: {Preview(material.Content)}");
            }
        }

        return builder.ToString();
    }

    private static object ParseJsonOrText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            return JsonNode.Parse(value) ?? value;
        }
        catch
        {
            return value;
        }
    }

    private static string ComputeContentHash(string content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty)));
    }

    private static string NormalizeArchiveRelativePath(string relativePath)
    {
        var segments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !string.Equals(segment, ".", StringComparison.Ordinal) &&
                              !string.Equals(segment, "..", StringComparison.Ordinal))
            .ToArray();

        return segments.Length == 0 ? "unknown.txt" : string.Join('/', segments);
    }

    private static void WriteUtf8Entry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string? Preview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 240 ? normalized : normalized[..240] + "...";
    }

    private static byte[] BuildArchive(
        IReadOnlyDictionary<string, byte[]> files,
        ArtifactSerializationRequest request)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files)
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(pair.Value);
            }

            WriteUtf8Entry(
                archive,
                $"{StructuredPackageRoot}/manifest.json",
                request.TemplatePackage.ManifestJson);

            foreach (var slice in request.TemplatePackage.OntologySlices)
            {
                WriteUtf8Entry(
                    archive,
                    $"{StructuredPackageRoot}/{NormalizeArchiveRelativePath(slice.RelativePath)}",
                    BuildOptimizedContent(slice.Content, request));
            }

            foreach (var skill in request.TemplatePackage.RequiredSkills)
            {
                WriteUtf8Entry(
                    archive,
                    $"{StructuredPackageRoot}/{NormalizeArchiveRelativePath(skill.RelativePath)}",
                    BuildOptimizedContent(skill.Content, request));
            }

            var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Fastest);
            using var readmeStream = new StreamWriter(readmeEntry.Open(), Encoding.UTF8);
            readmeStream.WriteLine("This archive contains structure-aligned V1 deliverables.");
            readmeStream.WriteLine($"Template package root: {StructuredPackageRoot}/");
            readmeStream.WriteLine($"Files: {string.Join(", ", files.Keys)}");
        }

        return stream.ToArray();
    }
}
