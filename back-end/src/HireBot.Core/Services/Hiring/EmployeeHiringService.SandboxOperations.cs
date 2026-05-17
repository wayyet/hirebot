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


    /// <summary>
    /// 将 MCP 配置上传到沙箱 /admin/workspace/mcp 接口。
    /// 配置从 OpenSandbox:McpConfig 读取；Enabled=false 或未配置时调用方应跳过。
    /// 上传失败为非致命错误，不影响沙箱整体初始化流程。
    /// </summary>
    private async Task<RemoteCallResult<SandboxMcpConfigResponse>> UploadSandboxMcpConfigAsync(
        string hireId,
        string ownerSubject,
        SandboxWorkspaceMcpConfig mcpConfig,
        CancellationToken cancellationToken)
    {
        var gatewayTargetResult = await ResolveSandboxGatewayTargetAsync(hireId, ownerSubject, cancellationToken);
        if (!gatewayTargetResult.Success || gatewayTargetResult.Data is null)
        {
            return RemoteCallResult<SandboxMcpConfigResponse>.Failure(gatewayTargetResult.Code, gatewayTargetResult.Message);
        }

        var call = await kingCrabHttpClient.SendForJsonAsync<SandboxMcpConfigResponse>(
            HttpMethod.Put,
            "/admin/workspace/mcp",
            mcpConfig,
            ownerSubject,
            cancellationToken,
            useHireBotApiPrefix: false,
            absoluteBaseUrl: gatewayTargetResult.Data.GatewayEndpoint);

        return call.Success && call.Data is not null
            ? RemoteCallResult<SandboxMcpConfigResponse>.Ok(call.Data)
            : RemoteCallResult<SandboxMcpConfigResponse>.Failure(call.StatusCode, call.Message);
    }

    /// <summary>
    /// 从配置中读取 MCP 设置。Enabled=false 或节不存在时返回 null，表示无需上传。
    /// </summary>
    private SandboxWorkspaceMcpConfig? ReadMcpConfig()
    {
        var config = configuration.GetSection("OpenSandbox:McpConfig").Get<SandboxWorkspaceMcpConfig>();
        return config?.Enabled == true ? config : null;
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

    /// <summary>
    /// 接收前端直传的模板包 ZIP，上传到雇佣沙箱工作区的 uploads/template-packages 目录。
    /// 返回沙箱内文件路径和可直接嵌入 WS 消息的 [FILE_URL:...] 标记，供前端在 WebSocket 引导消息中使用。
    /// </summary>
    public async Task<ApiResponse<HiringTemplatePackageUploadResultDto>> UploadTemplatePackageFromClientAsync(
        string hireId,
        Stream packageStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringTemplatePackageUploadResultDto>.ErrorResponse(400, error);
        }

        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ApiResponse<HiringTemplatePackageUploadResultDto>.ErrorResponse(400, "仅支持 .zip 格式的模板包");
        }

        // 读取流内容
        byte[] archiveBytes;
        using (var ms = new MemoryStream())
        {
            await packageStream.CopyToAsync(ms, cancellationToken);
            archiveBytes = ms.ToArray();
        }

        if (archiveBytes.Length == 0)
        {
            return ApiResponse<HiringTemplatePackageUploadResultDto>.ErrorResponse(400, "上传的模板包内容为空");
        }

        var ownerSubject = ResolveOwnerByHireId(normalizedHireId);
        var sandboxRole = ResolveSandboxRole(normalizedHireId);

        var uploadResult = await sandboxService.UploadWorkspaceFileAsync(
            new SandboxWorkspaceUploadRequestDto
            {
                ScopeType = SandboxScopeTypes.Hire,
                ScopeKey = normalizedHireId,
                SandboxRole = sandboxRole,
                OwnerSubject = ownerSubject,
                TargetDir = "uploads/template-packages",
                FileName = Path.GetFileName(fileName),
                Content = archiveBytes,
                ContentType = "application/zip"
            },
            cancellationToken);

        if (!uploadResult.Success || uploadResult.Data is null)
        {
            return ApiResponse<HiringTemplatePackageUploadResultDto>.ErrorResponse(
                uploadResult.Code,
                uploadResult.Message);
        }

        var cleanFileName = Path.GetFileName(fileName);
        var workspacePath = $"{uploadResult.Data.WorkspaceDir.TrimEnd('/')}/{cleanFileName}";
        var fileMarker = $"[FILE_URL:{workspacePath}]";

        logger.LogInformation(
            "[Hiring] Template package uploaded to workspace. HireId={HireId} WorkspaceDir={WorkspaceDir} FileName={FileName} SizeBytes={SizeBytes}",
            normalizedHireId, uploadResult.Data.WorkspaceDir, cleanFileName, archiveBytes.Length);

        return ApiResponse<HiringTemplatePackageUploadResultDto>.SuccessResponse(
            new HiringTemplatePackageUploadResultDto(
                WorkspaceDir: uploadResult.Data.WorkspaceDir,
                FileName: cleanFileName,
                WorkspacePath: workspacePath,
                FileMarker: fileMarker,
                SizeBytes: archiveBytes.Length));
    }

}
