using System.Text.Json;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Services.Security;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Hiring;

/// <summary>
/// 雇佣阶段状态服务实现（基于 3 张独立轻量表）。
/// </summary>
internal sealed class HiringStageService(
    HireBotDbContext dbContext,
    ISecretProtector secretProtector,
    ILogger<HiringStageService> logger) : IHiringStageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<HiringStageProgressDto?> GetStageProgressAsync(string hireId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.HiringStageProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        return entity is null
            ? null
            : new HiringStageProgressDto(
                entity.HireId,
                entity.CurrentStage,
                entity.PackagingTestCasesStatus,
                entity.UpdatedAtUtc,
                entity.UpdatedBy);
    }

    public async Task UpdateStageProgressAsync(
        string hireId,
        string currentStage,
        string? testCasesStatus = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.HiringStageProgresses
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (entity is null)
        {
            dbContext.HiringStageProgresses.Add(new HiringStageProgressEntity
            {
                HireId = hireId,
                CurrentStage = currentStage,
                PackagingTestCasesStatus = testCasesStatus,
                UpdatedAtUtc = now,
                UpdatedBy = "system"
            });
        }
        else
        {
            entity.CurrentStage = currentStage;
            if (testCasesStatus is not null)
            {
                entity.PackagingTestCasesStatus = testCasesStatus;
            }
            entity.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetStructuredDataAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.HiringStructuredData
            .AsNoTracking()
            .Where(x => x.HireId == hireId)
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.FieldKey,
            x => x.FieldValue,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveStructuredDataAsync(
        string hireId,
        IReadOnlyDictionary<string, string?> data,
        CancellationToken cancellationToken = default)
    {
        // 删除旧数据
        var existingRows = await dbContext.HiringStructuredData
            .Where(x => x.HireId == hireId)
            .ToListAsync(cancellationToken);

        dbContext.HiringStructuredData.RemoveRange(existingRows);

        // 插入新数据
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, value) in data)
        {
            dbContext.HiringStructuredData.Add(new HiringStructuredDataEntity
            {
                HireId = hireId,
                FieldKey = key,
                FieldValue = value,
                CollectedAtUtc = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<HiringExternalSystemConfigDto?> GetExternalConfigAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.HiringExternalConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        if (entity is null || entity.ConfigJson == "{}")
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<HiringExternalSystemConfigState>(entity.ConfigJson, JsonOptions);
            return state?.ToDto(secretProtector);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize external config for HireId={HireId}", hireId);
            return null;
        }
    }

    public async Task SaveExternalConfigAsync(
        string hireId,
        HiringExternalSystemConfigDto config,
        CancellationToken cancellationToken = default)
    {
        var state = HiringExternalSystemConfigState.FromDto(config, secretProtector);
        var configJson = JsonSerializer.Serialize(state, JsonOptions);

        var entity = await dbContext.HiringExternalConfigs
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (entity is null)
        {
            dbContext.HiringExternalConfigs.Add(new HiringExternalConfigEntity
            {
                HireId = hireId,
                ConfigJson = configJson,
                UpdatedAtUtc = now,
                UpdatedBy = "system"
            });
        }
        else
        {
            entity.ConfigJson = configJson;
            entity.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<HiringSkillLinkConfigDto?> GetSkillLinkConfigAsync(
        string hireId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.HiringSkillLinkConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        if (entity is null || entity.ConfigJson == "{}")
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<HiringSkillLinkConfigState>(entity.ConfigJson, JsonOptions);
            return state?.ToDto();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize skill link config for HireId={HireId}", hireId);
            return null;
        }
    }

    public async Task SaveSkillLinkConfigAsync(
        string hireId,
        HiringSkillLinkConfigDto config,
        CancellationToken cancellationToken = default)
    {
        var state = HiringSkillLinkConfigState.FromDto(config);
        var configJson = JsonSerializer.Serialize(state, JsonOptions);

        var entity = await dbContext.HiringSkillLinkConfigs
            .FirstOrDefaultAsync(x => x.HireId == hireId, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (entity is null)
        {
            dbContext.HiringSkillLinkConfigs.Add(new HiringSkillLinkConfigEntity
            {
                HireId = hireId,
                ConfigJson = configJson,
                UpdatedAtUtc = now,
                UpdatedBy = "system"
            });
        }
        else
        {
            entity.ConfigJson = configJson;
            entity.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
