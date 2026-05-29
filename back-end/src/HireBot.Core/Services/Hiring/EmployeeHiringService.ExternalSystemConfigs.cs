using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Core.Services.Hiring;

internal sealed partial class EmployeeHiringService
{
    public async Task<ApiResponse<HiringExternalSystemConfigDto>> GetExternalSystemConfigAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringExternalSystemConfigDto>.ErrorResponse(400, error);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken)
                             ?? hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringExternalSystemConfigDto>.ErrorResponse(404, "未找到该雇佣流程的运行时状态");
        }

        runtimeContext = await EnsureExternalSystemConfigHydratedAsync(runtimeContext, cancellationToken);
        return ApiResponse<HiringExternalSystemConfigDto>.SuccessResponse(
            runtimeContext.ExternalSystemConfig?.ToDto() ?? new HiringExternalSystemConfigDto());
    }

    public async Task<ApiResponse<HiringExternalSystemConfigDto>> SaveExternalSystemConfigAsync(
        string hireId,
        HiringExternalSystemConfigDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeHireId(hireId, out var normalizedHireId, out var error))
        {
            return ApiResponse<HiringExternalSystemConfigDto>.ErrorResponse(400, error);
        }

        var runtimeContext = await RefreshRuntimeProgressAsync(normalizedHireId, cancellationToken)
                             ?? hiringRuntimeStore.Get(normalizedHireId);
        if (runtimeContext is null)
        {
            return ApiResponse<HiringExternalSystemConfigDto>.ErrorResponse(404, "未找到该雇佣流程的运行时状态");
        }

        runtimeContext = await EnsureExternalSystemConfigHydratedAsync(runtimeContext, cancellationToken);

        var normalizedState = HiringExternalSystemConfigState.FromDto(
            request,
            runtimeContext.ExternalSystemConfig,
            secretProtector);
        var persistedState = normalizedState.HasAnyConfig ? normalizedState : null;

        runtimeContext = runtimeContext with
        {
            ExternalSystemConfig = persistedState
        };
        runtimeContext = ApplyConversationProgressToTemplatePackage(runtimeContext);
        hiringRuntimeStore.Upsert(runtimeContext);

        await UpsertExternalSystemConfigMetadataAsync(runtimeContext.SandboxId, persistedState, cancellationToken);

        return ApiResponse<HiringExternalSystemConfigDto>.SuccessResponse(
            persistedState?.ToDto() ?? new HiringExternalSystemConfigDto(),
            persistedState is null ? "已清空外部系统配置" : "已保存外部系统配置");
    }

    private async Task<HiringRuntimeContext> EnsureExternalSystemConfigHydratedAsync(
        HiringRuntimeContext runtimeContext,
        CancellationToken cancellationToken)
    {
        if (runtimeContext.ExternalSystemConfig is not null)
        {
            return runtimeContext;
        }

        var sandboxConfig = await LoadExternalSystemConfigFromSandboxAsync(runtimeContext.SandboxId, cancellationToken);
        if (sandboxConfig is null)
        {
            return runtimeContext;
        }

        var hydratedRuntimeContext = ApplyConversationProgressToTemplatePackage(runtimeContext with
        {
            ExternalSystemConfig = sandboxConfig
        });
        hiringRuntimeStore.Upsert(hydratedRuntimeContext);
        return hydratedRuntimeContext;
    }

    private static HiringExternalSystemConfigState? DeserializeExternalSystemConfig(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null
            || !metadata.TryGetValue(SandboxMetaKeys.ExternalSystemConfig, out var rawState)
            || string.IsNullOrWhiteSpace(rawState))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<HiringExternalSystemConfigState>(rawState, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<HiringExternalSystemConfigState?> LoadExternalSystemConfigFromSandboxAsync(
        string sandboxId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sandboxId))
        {
            return null;
        }

        var metadata = await dbContext.SandboxInstances
            .AsNoTracking()
            .Where(item => item.SandboxId == sandboxId)
            .Select(item => item.Metadata)
            .FirstOrDefaultAsync(cancellationToken);

        return DeserializeExternalSystemConfig(metadata);
    }

    private async Task UpsertExternalSystemConfigMetadataAsync(
        string sandboxId,
        HiringExternalSystemConfigState? state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sandboxId))
        {
            return;
        }

        var sandboxInstance = await dbContext.SandboxInstances
            .FirstOrDefaultAsync(item => item.SandboxId == sandboxId, cancellationToken);
        if (sandboxInstance is null)
        {
            return;
        }

        sandboxInstance.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (state is null)
        {
            sandboxInstance.Metadata.Remove(SandboxMetaKeys.ExternalSystemConfig);
        }
        else
        {
            sandboxInstance.Metadata[SandboxMetaKeys.ExternalSystemConfig] = JsonSerializer.Serialize(state, JsonOptions);
        }

        sandboxInstance.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
