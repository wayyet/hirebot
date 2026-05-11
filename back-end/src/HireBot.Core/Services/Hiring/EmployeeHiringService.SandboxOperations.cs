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
    private Task<RemoteCallResult<SystemSkillUploadResult>> UploadDiscoverySystemSkillAsync(
        string hireId,
        DiscoverySkillDefinition discoverySkill,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        return UploadSystemSkillPackageAsync(
            hireId,
            ownerSubject,
            BuildSystemSkillUploadPayload(discoverySkill),
            cancellationToken);
    }

    private Task<RemoteCallResult<TemplatePackageUploadResult>> UploadTemplatePackageAsync(
        string hireId,
        TemplatePackageDefinition templatePackage,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        return UploadTemplatePackageViaDigitalEmployeeAsync(
            hireId,
            templatePackage,
            ownerSubject,
            cancellationToken);
    }

    private async Task<RemoteCallResult<TemplatePackageUploadResult>> UploadTemplatePackageViaDigitalEmployeeAsync(
        string hireId,
        TemplatePackageDefinition templatePackage,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var archiveBytes = BuildDigitalEmployeeArchive(templatePackage);
        var fileName = $"{templatePackage.PackageId}-{templatePackage.PackageVersion}.zip";
        var uploadCall = await UploadSandboxArchiveAsync(
            hireId,
            ownerSubject,
            archiveBytes,
            fileName,
            cancellationToken);
        if (!uploadCall.Success || uploadCall.Data is null)
        {
            return RemoteCallResult<TemplatePackageUploadResult>.Failure(uploadCall.StatusCode, uploadCall.Message);
        }

        if (!uploadCall.Data.Success)
        {
            return RemoteCallResult<TemplatePackageUploadResult>.Failure(
                502,
                string.IsNullOrWhiteSpace(uploadCall.Data.Error) ? "数字员工模板包上传失败" : uploadCall.Data.Error);
        }

        return RemoteCallResult<TemplatePackageUploadResult>.Ok(new TemplatePackageUploadResult(
            HireId: hireId,
            SandboxId: string.Empty,
            PackageId: templatePackage.PackageId,
            PackageVersion: templatePackage.PackageVersion,
            PackageHash: templatePackage.PackageHash,
            InstalledPath: "workspace"));
    }

    private static SystemSkillUploadPayload BuildSystemSkillUploadPayload(DiscoverySkillDefinition discoverySkill)
    {
        return new SystemSkillUploadPayload(
            SkillId: discoverySkill.SkillId,
            SkillVersion: discoverySkill.SkillVersion,
            SkillHash: discoverySkill.SkillHash,
            Files: discoverySkill.Files
                .Select(file => new SystemSkillFileUploadPayload(
                    RelativePath: file.RelativePath,
                    ContentHash: file.ContentHash,
                    Content: file.Content))
                .ToArray(),
            StageRules: discoverySkill.StageRules
                .Select(rule => new SystemSkillStageRuleUploadPayload(
                    Stage: rule.Stage,
                    SkillName: rule.SkillName,
                    Description: rule.Description,
                    RequiredFields: rule.RequiredFields))
                .ToArray());
    }

    private async Task<ApiResponse<SystemSkillUploadPayload>> BuildEvaluationSkillUploadPayloadAsync(
        string? skillRootPath,
        CancellationToken cancellationToken)
    {
        SystemSkillPackage package;
        try
        {
            package = await systemSkillRegistry.LoadRequiredAsync(
                EvaluationSkillId,
                skillRootPath,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<SystemSkillUploadPayload>.ErrorResponse(422, ex.Message);
        }

        if (package.StageRules.Count == 0)
        {
            return ApiResponse<SystemSkillUploadPayload>.ErrorResponse(422, "evaluation system skill must declare stage rules");
        }

        if (package.Files.Count == 0)
        {
            return ApiResponse<SystemSkillUploadPayload>.ErrorResponse(422, "evaluation skill payload is empty");
        }

        var orderedFiles = package.Files
            .Select(file => new SystemSkillFileUploadPayload(
                RelativePath: file.RelativePath,
                ContentHash: file.ContentHash,
                Content: file.Content))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var payload = new SystemSkillUploadPayload(
            SkillId: package.SkillId,
            SkillVersion: package.Version,
            SkillHash: package.SkillHash,
            Files: orderedFiles,
            StageRules: package.StageRules
                .Select(rule => new SystemSkillStageRuleUploadPayload(
                    Stage: rule.Stage,
                    SkillName: rule.SkillName,
                    Description: rule.Description,
                    RequiredFields: rule.RequiredFields))
                .ToArray());

        return ApiResponse<SystemSkillUploadPayload>.SuccessResponse(payload);
    }

    private static DiscoverySkillDefinition BuildDiscoverySkillFromUploadPayload(SystemSkillUploadPayload payload)
    {
        var files = payload.Files
            .Select(file => new DiscoverySkillFileAsset(
                RelativePath: file.RelativePath,
                Content: file.Content,
                ContentHash: file.ContentHash))
            .ToArray();
        var stageRules = payload.StageRules
            .Select(rule => new DiscoveryStageRule(
                Stage: rule.Stage,
                SkillName: rule.SkillName,
                Description: rule.Description,
                RequiredFields: rule.RequiredFields))
            .ToArray();
        var rootContent = files
            .FirstOrDefault(file => file.RelativePath.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
            ?.Content
            ?? $"# {payload.SkillId}";

        return new DiscoverySkillDefinition(
            SkillId: payload.SkillId,
            SkillVersion: payload.SkillVersion,
            SkillHash: payload.SkillHash,
            SkillRootPath: payload.SkillId,
            SkillContent: rootContent,
            Files: files,
            StageRules: stageRules);
    }

    private static TemplatePackageDefinition BuildEvaluationWorkspaceTemplatePackage()
    {
        const string manifestJson = """
{
  "template_id": "evaluation-expert",
  "display_name": "Evaluation Expert Workspace",
  "description": "Workspace package for evaluator sandbox"
}
""";
        var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);

        return new TemplatePackageDefinition(
            RequestedTemplateId: EvaluationWorkspaceTemplateId,
            PackageId: EvaluationWorkspaceTemplateId,
            PackageVersion: EvaluationSkillVersion,
            PackageHash: ComputeContentHash(manifestJson),
            SourceArchive: null,
            PackageRootPath: "evaluation-workspace",
            ManifestJson: manifestJson,
            DisplayName: EvaluationWorkspaceTemplateName,
            Description: "Evaluator sandbox template package",
            PackageFiles:
            [
                new TemplatePackageFileAsset(
                    RelativePath: "manifest.json",
                    Content: manifestBytes,
                    ContentHash: ComputeContentHash(manifestJson))
            ],
            OntologySlices: [],
            RequiredSkills: [],
            EntrySkill: null,
            StageRules: []);
    }

    private static DiscoverySkillDefinition BuildEvaluationWorkspaceDiscoverySkill()
    {
        const string rootSkillContent = """
# evaluation-expert

This is the bootstrap skill for evaluation sandbox orchestration.
""";
        var stageRule = new DiscoveryStageRule(
            Stage: "evaluation",
            SkillName: "evaluation_orchestrator",
            Description: "Evaluate target sandbox and output PASS or FAIL.",
            RequiredFields: ["evaluation_goal"]);

        return new DiscoverySkillDefinition(
            SkillId: EvaluationSkillId,
            SkillVersion: EvaluationSkillVersion,
            SkillHash: ComputeContentHash(rootSkillContent),
            SkillRootPath: "evaluation-expert",
            SkillContent: rootSkillContent,
            Files:
            [
                new DiscoverySkillFileAsset(
                    RelativePath: "SKILL.md",
                    Content: rootSkillContent,
                    ContentHash: ComputeContentHash(rootSkillContent))
            ],
            StageRules: [stageRule]);
    }

    /// <summary>
    /// 为雇佣流程创建托管沙箱，并同步等待沙箱就绪（最多 180 秒）。
    /// 此方法会阻塞直到沙箱状态变为 "Running" 且 GatewayEndpoint 可用。
    /// </summary>
    /// <param name="sandboxRole">沙箱角色，如 "hiring" 或 "evaluation-evaluator"</param>
    /// <param name="ownerSubject">沙箱所有者标识，格式为 "tenant:operator" 或 JWT sub claim</param>
    /// <param name="tenantId">租户 ID</param>
    /// <param name="operatorId">操作员 ID</param>
    /// <param name="useCase">用例描述，用于审计和追踪</param>
    /// <returns>包含 hireId、sandboxId、状态和网关地址的绑定信息</returns>
    private async Task<ApiResponse<ProvisionedSandboxBinding>> ProvisionManagedHireSandboxAsync(
        string sandboxRole,
        string ownerSubject,
        string tenantId,
        string operatorId,
        string? templateId,
        string? useCase,
        CancellationToken cancellationToken)
    {
        var hireId = $"hire-{Guid.NewGuid():N}";
        var createResult = await sandboxService.CreateAsync(
            new SandboxCreateRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = sandboxRole,
                OwnerSubject = ownerSubject,
                TenantId = tenantId,
                OperatorId = operatorId,
                ProvisioningMode = "managed",
                UseCase = useCase,
                TemplateId = templateId
            },
            cancellationToken);
        if (!createResult.Success || createResult.Data is null)
        {
            return ApiResponse<ProvisionedSandboxBinding>.ErrorResponse(createResult.Code, createResult.Message);
        }

        var readyResult = await WaitForManagedSandboxReadyAsync(createResult.Data, cancellationToken);
        if (!readyResult.Success || readyResult.Data is null)
        {
            return ApiResponse<ProvisionedSandboxBinding>.ErrorResponse(readyResult.Code, readyResult.Message);
        }

        return ApiResponse<ProvisionedSandboxBinding>.SuccessResponse(
            new ProvisionedSandboxBinding(
                hireId,
                readyResult.Data.SandboxId,
                readyResult.Data.State,
                readyResult.Data.GatewayEndpoint));
    }

    /// <summary>
    /// 轮询等待托管沙箱就绪（状态为 "Running" 且 GatewayEndpoint 非空）。
    /// 最多轮询 36 次，每次间隔 5 秒，总计最多等待 180 秒。
    /// </summary>
    /// <param name="instance">沙箱实例初始状态</param>
    /// <returns>就绪后的沙箱实例信息，或超时错误</returns>
    private async Task<ApiResponse<SandboxInstanceDto>> WaitForManagedSandboxReadyAsync(
        SandboxInstanceDto instance,
        CancellationToken cancellationToken)
    {
        if (string.Equals(instance.State, "Running", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(instance.GatewayEndpoint))
        {
            return ApiResponse<SandboxInstanceDto>.SuccessResponse(instance);
        }

        for (var attempt = 0; attempt < 36; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var refreshResult = await sandboxService.RefreshAsync(
                new SandboxInstanceLookupRequestDto
                {
                    SandboxId = instance.SandboxId
                },
                cancellationToken);
            if (!refreshResult.Success || refreshResult.Data is null)
            {
                return ApiResponse<SandboxInstanceDto>.ErrorResponse(refreshResult.Code, refreshResult.Message);
            }

            if (string.Equals(refreshResult.Data.State, "Running", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(refreshResult.Data.GatewayEndpoint))
            {
                return ApiResponse<SandboxInstanceDto>.SuccessResponse(refreshResult.Data);
            }
        }

        return ApiResponse<SandboxInstanceDto>.ErrorResponse(504, "sandbox 启动超时，网关 endpoint 尚未就绪");
    }

    private async Task<RemoteCallResult<DigitalEmployeeUploadResponse>> UploadSandboxArchiveAsync(
        string hireId,
        string ownerSubject,
        byte[] archiveBytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        var gatewayTargetResult = await ResolveSandboxGatewayTargetAsync(hireId, ownerSubject, cancellationToken);
        if (!gatewayTargetResult.Success || gatewayTargetResult.Data is null)
        {
            return RemoteCallResult<DigitalEmployeeUploadResponse>.Failure(gatewayTargetResult.Code, gatewayTargetResult.Message);
        }

        var call = await kingCrabHttpClient.SendMultipartForJsonAsync<DigitalEmployeeUploadResponse>(
            "/admin/digital-employee/upload",
            "file",
            fileName,
            archiveBytes,
            "application/zip",
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: gatewayTargetResult.Data.GatewayEndpoint);

        return call.Success && call.Data is not null
            ? RemoteCallResult<DigitalEmployeeUploadResponse>.Ok(call.Data)
            : RemoteCallResult<DigitalEmployeeUploadResponse>.Failure(call.StatusCode, call.Message);
    }

    private async Task<RemoteCallResult<SystemSkillUploadResult>> UploadSystemSkillPackageAsync(
        string hireId,
        string ownerSubject,
        SystemSkillUploadPayload payload,
        CancellationToken cancellationToken)
    {
        var archiveBytes = BuildSystemSkillArchive(payload);
        var uploadCall = await UploadSandboxArchiveAsync(
            hireId,
            ownerSubject,
            archiveBytes,
            $"{payload.SkillId}-{payload.SkillVersion}.zip",
            cancellationToken);
        if (!uploadCall.Success || uploadCall.Data is null)
        {
            return RemoteCallResult<SystemSkillUploadResult>.Failure(uploadCall.StatusCode, uploadCall.Message);
        }

        if (!uploadCall.Data.Success)
        {
            return RemoteCallResult<SystemSkillUploadResult>.Failure(
                502,
                string.IsNullOrWhiteSpace(uploadCall.Data.Error) ? "system skill 上传失败" : uploadCall.Data.Error);
        }

        return RemoteCallResult<SystemSkillUploadResult>.Ok(new SystemSkillUploadResult(
            HireId: hireId,
            SandboxId: string.Empty,
            SkillId: payload.SkillId,
            SkillVersion: payload.SkillVersion,
            SkillHash: payload.SkillHash,
            InstalledPath: "workspace/skills",
            LoadedStageSkills: payload.StageRules
                .Select(rule => new StageSkillMappingDto(
                    rule.Stage,
                    rule.SkillName,
                    rule.RequiredFields,
                    rule.Description))
                .ToArray()));
    }

    private async Task SetSandboxInitializedAsync(string sandboxId, CancellationToken cancellationToken)
    {
        var instance = await dbContext.SandboxInstances
            .FirstOrDefaultAsync(item => item.SandboxId == sandboxId, cancellationToken);
        if (instance is not null && !instance.IsInitialized)
        {
            instance.IsInitialized = true;
            instance.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Sandbox marked as initialized. SandboxId={SandboxId}", sandboxId);
        }
    }

    private async Task<ApiResponse<SandboxGatewayTarget>> ResolveSandboxGatewayTargetAsync(
        string hireId,
        string ownerSubject,
        CancellationToken cancellationToken)
    {
        var sandboxRole = ResolveSandboxRole(hireId);
        var refreshResult = await sandboxService.RefreshAsync(
            new SandboxInstanceLookupRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = hireId,
                SandboxRole = sandboxRole,
                OwnerSubject = ownerSubject
            },
            cancellationToken);
        if (!refreshResult.Success || refreshResult.Data is null)
        {
            return ApiResponse<SandboxGatewayTarget>.ErrorResponse(refreshResult.Code, refreshResult.Message);
        }

        if (!string.Equals(refreshResult.Data.State, "Running", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<SandboxGatewayTarget>.ErrorResponse(409, "sandbox 尚未就绪");
        }

        if (string.IsNullOrWhiteSpace(refreshResult.Data.GatewayEndpoint))
        {
            return ApiResponse<SandboxGatewayTarget>.ErrorResponse(409, "sandbox gateway endpoint 尚未就绪");
        }

        return ApiResponse<SandboxGatewayTarget>.SuccessResponse(
            new SandboxGatewayTarget(
                refreshResult.Data.SandboxId,
                refreshResult.Data.GatewayEndpoint));
    }

    private static byte[] BuildSystemSkillArchive(SystemSkillUploadPayload payload)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in payload.Files)
            {
                if (string.IsNullOrWhiteSpace(file.RelativePath))
                {
                    continue;
                }

                var normalizedPath = "skills/" + payload.SkillId.Trim().Trim('/') + "/" + file.RelativePath.TrimStart('/', '\\').Replace('\\', '/');
                if (!TryNormalizeArchiveEntryPath(normalizedPath, out normalizedPath))
                {
                    continue;
                }

                var contentBytes = Encoding.UTF8.GetBytes(file.Content ?? string.Empty);
                var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                entryStream.Write(contentBytes, 0, contentBytes.Length);
            }
        }

        return memoryStream.ToArray();
    }

}
