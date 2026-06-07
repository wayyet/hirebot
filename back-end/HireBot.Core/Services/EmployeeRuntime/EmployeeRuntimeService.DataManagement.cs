using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Migration;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Services.EmployeeRuntime;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text.Json;

namespace HireBot.Core.Services.EmployeeRuntime;

public sealed partial class EmployeeRuntimeService
{
    /// <summary>
    /// 批量 Upsert 员工到 DB。
    /// </summary>
    private async Task TryUpsertInstanceRecordsAsync(IReadOnlyList<EmployeeDetailDto> employees, CancellationToken cancellationToken)
    {
        foreach (var employee in employees)
        {
            try
            {
                await UpsertInstanceRecordAsync(employee, cancellationToken: cancellationToken);
            }
            catch
            {
                // 单条失败不影响其他
            }
        }
    }

    /// 加载持久化的运行时员工数据。
    /// </summary>
    private async Task<IReadOnlyList<EmployeeDetailDto>> LoadPersistedRuntimeEmployeesAsync(
        string owner,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = dbContext.Instances
                .AsNoTracking()
                .Where(item => item.OwnerUserId == owner)
                .OrderByDescending(item => item.UpdatedAt);
            return await LoadInstancesAsEmployeesAsync(query, owner, cancellationToken);
        }
        catch
        {
            // Runtime snapshots are a persistence enhancement. Existing local databases may not have the column until migrations run.
            return [];
        }
    }

    /// <summary>
    /// 将 IQueryable 查询结果反序列化为员工列表。
    /// </summary>
    private async Task<IReadOnlyList<EmployeeDetailDto>> LoadInstancesAsEmployeesAsync(
        IQueryable<InstanceEntity> query,
        string? owner = null,
        CancellationToken cancellationToken = default)
    {
        var instances = await query.ToArrayAsync(cancellationToken);
        var employees = new List<EmployeeDetailDto>();
        
        // 批量查询创建人信息
        var ownerUserIds = instances.Select(i => i.OwnerUserId).Distinct().ToList();
        var creators = await dbContext.AppUsers
            .AsNoTracking()
            .Where(u => ownerUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);
        
        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var employee = !string.IsNullOrWhiteSpace(instance.RuntimeSnapshotJson)
                ? DeserializeEmployeeSnapshot(instance.RuntimeSnapshotJson)
                : await BuildEmployeeFromInstanceRecordAsync(instance, cancellationToken);
            if (!HasRequiredIdentity(employee))
            {
                employee = await BuildEmployeeFromInstanceRecordAsync(instance, cancellationToken);
            }

            if (employee is null)
            {
                continue;
            }

            // 始终使用 DB 实体的 CreatedAt 覆盖快照中的值，避免历史快照缺少时间精度
            employee = employee with { CreatedAt = instance.CreatedAt };
            
            // 填充描述信息（优先使用 Description 字段，如果为空则从 DescribeDocument 提取）
            string? description = instance.Description;
            if (string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(instance.DescribeDocument))
            {
                // 尝试提取业务定位一句话
                description = ExtractBusinessPositioningOneLiner(instance.DescribeDocument);
                // 如果没有，尝试提取 CardIntro（第1和第4节）
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = ExtractCardIntro(instance.DescribeDocument);
                }
                // 如果还是没有，直接截取前 300 字符
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = instance.DescribeDocument.Length > 300
                        ? instance.DescribeDocument.Substring(0, 300) + "..."
                        : instance.DescribeDocument;
                }
            }
            // 如果 Description 和 DescribeDocument 都为空，使用 CardIntro
            if (string.IsNullOrWhiteSpace(description))
            {
                description = employee.CardIntro;
            }
            // 填充描述
            if (!string.IsNullOrWhiteSpace(description))
            {
                employee = employee with { Description = description };
            }
            
            // 填充创建人信息
            if (creators.TryGetValue(instance.OwnerUserId, out var creator))
            {
                employee = employee with
                {
                    CreatedBy = new HireBot.Abstraction.Models.User.CreatorRef
                    {
                        Username = creator.Username,
                        DisplayName = creator.DisplayName,
                        FamilyName = creator.FamilyName,
                        GivenName = creator.GivenName
                    }
                };
            }

            if (owner is not null &&
                !string.Equals(employee.OwnerUserId, owner, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            employees.Add(employee);
        }

        return employees;
    }

    /// <summary>
    /// 从持久化实例记录恢复单个员工。
    /// </summary>
    private async Task<EmployeeDetailDto?> LoadPersistedRuntimeEmployeeAsync(
        string owner,
        string employeeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var instance = await dbContext.Instances
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.OwnerUserId == owner && item.InstanceId == employeeId,
                    cancellationToken);

            if (instance is null)
            {
                return null;
            }

            var employee = !string.IsNullOrWhiteSpace(instance.RuntimeSnapshotJson)
                ? DeserializeEmployeeSnapshot(instance.RuntimeSnapshotJson)
                : await BuildEmployeeFromInstanceRecordAsync(instance, cancellationToken);

            // 始终使用 DB 实体的 CreatedAt 覆盖快照中的值，避免历史快照缺少时间精度
            if (employee is not null)
            {
                employee = employee with { CreatedAt = instance.CreatedAt };
                
                // 填充描述信息（优先从 DescribeDocument 提取简短描述，备用 CardIntro，最后截取原文）
                string? description = null;
                if (!string.IsNullOrWhiteSpace(instance.DescribeDocument))
                {
                    // 尝试提取业务定位一句话
                    description = ExtractBusinessPositioningOneLiner(instance.DescribeDocument);
                    // 如果没有，尝试提取 CardIntro（第1和第4节）
                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = ExtractCardIntro(instance.DescribeDocument);
                    }
                    // 如果还是没有，直接截取前 300 字符
                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = instance.DescribeDocument.Length > 300
                            ? instance.DescribeDocument.Substring(0, 300) + "..."
                            : instance.DescribeDocument;
                    }
                }
                // 如果 DescribeDocument 为空，使用 CardIntro
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = employee.CardIntro;
                }
                // 填充描述
                if (!string.IsNullOrWhiteSpace(description))
                {
                    employee = employee with { Description = description };
                }
                
                // 查询创建人信息
                var creator = await dbContext.AppUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == instance.OwnerUserId, cancellationToken);
                
                if (creator is not null)
                {
                    employee = employee with
                    {
                        CreatedBy = new HireBot.Abstraction.Models.User.CreatorRef
                        {
                            Username = creator.Username,
                            DisplayName = creator.DisplayName,
                            FamilyName = creator.FamilyName,
                            GivenName = creator.GivenName
                        }
                    };
                }
            }

            // DB 查询已保证 OwnerUserId == owner，此处无需再次校验快照内的 OwnerUserId，
            // 避免快照序列化格式不一致时导致误判为 null。
            return employee;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 按 owner 和 employeeId 加载单个员工（直接从 DB）。
    /// </summary>
    private async Task<EmployeeDetailDto?> ResolveEmployeeForOwnerAsync(
        string owner,
        string employeeId,
        CancellationToken cancellationToken)
    {
        return await LoadPersistedRuntimeEmployeeAsync(owner, employeeId.Trim(), cancellationToken);
    }

    /// <summary>
    /// 返回 owner 下的员工全集（直接从 DB）。
    /// </summary>
    private async Task<IReadOnlyList<EmployeeDetailDto>> ResolveOwnerEmployeesAsync(
        string owner,
        CancellationToken cancellationToken)
    {
        return await LoadPersistedRuntimeEmployeesAsync(owner, cancellationToken);
    }
    /// </summary>
    private async Task<EmployeeDetailDto?> ResolveDepartmentEmployeeForTenantAsync(
        string tenantId,
        string employeeId,
        CancellationToken cancellationToken)
    {
        var normalizedEmployeeId = employeeId.Trim();
        var instance = await dbContext.Instances
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId
                        && item.InstanceType == "department"
                        && item.InstanceId == normalizedEmployeeId,
                cancellationToken);
        if (instance is null)
        {
            return null;
        }

        var employee = !string.IsNullOrWhiteSpace(instance.RuntimeSnapshotJson)
            ? DeserializeEmployeeSnapshot(instance.RuntimeSnapshotJson)
            : await BuildEmployeeFromInstanceRecordAsync(instance, cancellationToken);

        // 始终使用 DB 实体的 CreatedAt 覆盖快照中的值，避免历史快照缺少时间精度
        if (employee is not null)
        {
            employee = employee with { CreatedAt = instance.CreatedAt };
            
            // 填充描述信息（优先从 DescribeDocument 提取简短描述，备用 CardIntro，最后截取原文）
            string? description = null;
            if (!string.IsNullOrWhiteSpace(instance.DescribeDocument))
            {
                // 尝试提取业务定位一句话
                description = ExtractBusinessPositioningOneLiner(instance.DescribeDocument);
                // 如果没有，尝试提取 CardIntro（第1和第4节）
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = ExtractCardIntro(instance.DescribeDocument);
                }
                // 如果还是没有，直接截取前 300 字符
                if (string.IsNullOrWhiteSpace(description))
                {
                    description = instance.DescribeDocument.Length > 300
                        ? instance.DescribeDocument.Substring(0, 300) + "..."
                        : instance.DescribeDocument;
                }
            }
            // 如果 DescribeDocument 为空，使用 CardIntro
            if (string.IsNullOrWhiteSpace(description))
            {
                description = employee.CardIntro;
            }
            // 填充描述
            if (!string.IsNullOrWhiteSpace(description))
            {
                employee = employee with { Description = description };
            }
            
            // 查询创建人信息
            var creator = await dbContext.AppUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == instance.OwnerUserId, cancellationToken);
            
            if (creator is not null)
            {
                employee = employee with
                {
                    CreatedBy = new HireBot.Abstraction.Models.User.CreatorRef
                    {
                        Username = creator.Username,
                        DisplayName = creator.DisplayName,
                        FamilyName = creator.FamilyName,
                        GivenName = creator.GivenName
                    }
                };
            }
        }

        return employee;
    }

    /// <summary>
    /// 反序列化员工快照。
    /// </summary>
    private static readonly JsonSerializerOptions SnapshotDeserializeOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static EmployeeDetailDto? DeserializeEmployeeSnapshot(string snapshot)
    {
        try
        {
            // 快照可能由 ASP.NET Core（camelCase）或内部（PascalCase）序列化生成，
            // 使用大小写不敏感选项确保两种格式都能正确反序列化。
            return JsonSerializer.Deserialize<EmployeeDetailDto>(snapshot, SnapshotDeserializeOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool HasRequiredIdentity(EmployeeDetailDto? employee)
    {
        if (employee is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(employee.EmployeeId) &&
               (!string.IsNullOrWhiteSpace(employee.SourceTemplateId) ||
                !string.IsNullOrWhiteSpace(employee.BasedOnTemplateId) ||
                !string.IsNullOrWhiteSpace(employee.RoleName));
    }

    /// <summary>
    /// 从实例记录构建员工详情。
    /// </summary>
    private async Task<EmployeeDetailDto?> BuildEmployeeFromInstanceRecordAsync(
        InstanceEntity instance,
        CancellationToken cancellationToken)
    {
        EmployeeDetailDto? source = null;
        if (!string.IsNullOrWhiteSpace(instance.FromInstanceId))
        {
            // 从 DB 读取源实例快照（不依赖内存 store）
            var fromInstance = await dbContext.Instances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InstanceId == instance.FromInstanceId, cancellationToken);
            if (fromInstance is not null && !string.IsNullOrWhiteSpace(fromInstance.RuntimeSnapshotJson))
            {
                source = DeserializeEmployeeSnapshot(fromInstance.RuntimeSnapshotJson);
            }
        }

        var status = NormalizeStatus(instance.Status, null) ?? "hired";
        var type = string.IsNullOrWhiteSpace(instance.InstanceType) ? "department" : instance.InstanceType;
        var templateId = instance.BasedOnTemplateId ?? source?.BasedOnTemplateId ?? source?.SourceTemplateId ?? "unknown-template";
        var roleName = source?.RoleName ?? templateId;
        IReadOnlyList<EmployeeCapabilityDto> capabilities = source?.Capabilities.Count > 0
            ? source.Capabilities.Select(item => item with { Ready = status is "live" }).ToArray()
            : [new EmployeeCapabilityDto("站内对话", status is "live")];

        return new EmployeeDetailDto(
            EmployeeId: instance.InstanceId,
            Nickname: source is null ? instance.InstanceId : $"{source.Nickname} 的分身",
            RoleName: roleName,
            SourceTemplate: source?.SourceTemplate ?? templateId,
            SourceTemplateId: source?.SourceTemplateId ?? templateId,
            InstanceType: type,
            Status: status,
            BasedOnTemplateId: instance.BasedOnTemplateId,
            FromInstanceId: instance.FromInstanceId,
            OwnerUserId: instance.OwnerUserId,
            DepartmentId: instance.DepartmentId,
            LifecycleStatus: MapStatusToLifecycleLabel(status),
            StageSummary: type is "personal_clone" or "private_branch"
                ? "个人分身已恢复，站内对话可用"
                : BuildStageSummary(status, instance.InstanceId),
            PrimarySignal: BuildPrimarySignal(status),
            SignalLevel: status is "hired" or "interning_ai" ? "warn" : "ok",
            OwningTeam: instance.DepartmentId,
            CreatedAt: instance.CreatedAt,
            InternshipStartAt: status is "live" ? DateOnly.FromDateTime(instance.CreatedAt.UtcDateTime).ToString("yyyy-MM-dd") : null,
            GraduatedAt: status is "live" ? DateOnly.FromDateTime(instance.UpdatedAt.UtcDateTime).ToString("yyyy-MM-dd") : null,
            TasksDone: 0,
            TasksTotal: 0,
            SatisfactionScore: null,
            PendingActions: [],
            Capabilities: capabilities,
            EvalPhase: null,
            EvalIteration: null,
            EvalMaxIterations: null,
            IsConfigured: capabilities.All(item => item.Ready),
            CardIntro: source?.CardIntro,
            Description: null); // 描述将在外层方法中统一填充
    }

    /// <summary>
    /// 加载 Fixture 包。
    /// </summary>
    private static async Task<FixtureBundle> LoadFixtureBundleAsync(string owner, CancellationToken cancellationToken)
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot) || !Directory.Exists(fixtureRoot))
        {
            return new FixtureBundle([], 0);
        }

        var directories = Directory.GetDirectories(fixtureRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var employees = new List<EmployeeDetailDto>();
        var seedIndex = 0;
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var instancePath = Path.Combine(directory, "instance.json");
            if (!File.Exists(instancePath))
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, "manifest.json");

            try
            {
                var instanceContent = await File.ReadAllTextAsync(instancePath, cancellationToken);
                using var instanceDoc = JsonDocument.Parse(instanceContent);
                var root = instanceDoc.RootElement;

                var employeeId = TryGetString(root, "employeeId");
                if (string.IsNullOrWhiteSpace(employeeId))
                {
                    continue;
                }

                var templateId = TryGetString(root, "templateId", "fixture");
                var hireId = TryGetString(root, "hireId", employeeId);
                var scenario = TryGetString(root, "scenario", "fixture-collaboration");
                var generatedAtUtc = TryGetString(root, "generatedAtUtc");
                var explicitStatus = TryGetString(root, "status");

                var displayName = templateId;
                var capabilityNames = new List<string>();
                if (File.Exists(manifestPath))
                {
                    var manifestContent = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                    using var manifestDoc = JsonDocument.Parse(manifestContent);
                    var manifestRoot = manifestDoc.RootElement;
                    displayName = TryGetString(manifestRoot, "display_name", templateId);

                    if (manifestRoot.TryGetProperty("skills", out var skillsElement) &&
                        skillsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var skill in skillsElement.EnumerateArray())
                        {
                            var skillName = TryGetString(skill, "name");
                            if (!string.IsNullOrWhiteSpace(skillName))
                            {
                                capabilityNames.Add(skillName);
                            }
                        }
                    }
                }

                if (capabilityNames.Count == 0)
                {
                    capabilityNames.Add("scenario_parser");
                    capabilityNames.Add("report_generator");
                }

                var status = NormalizeStatus(explicitStatus, null) ?? ResolveFixtureSeedStatus(seedIndex++);
                var createdAt = ResolveCreatedAt(generatedAtUtc, seedIndex);
                var isReady = status is "live";
                var capabilities = capabilityNames
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => new EmployeeCapabilityDto(name, isReady))
                    .ToArray();

                var employee = new EmployeeDetailDto(
                    EmployeeId: employeeId.Trim(),
                    Nickname: BuildFixtureNickname(displayName, employeeId),
                    RoleName: displayName,
                    SourceTemplate: displayName,
                    SourceTemplateId: templateId,
                    InstanceType: "department",
                    Status: status,
                    BasedOnTemplateId: templateId,
                    FromInstanceId: null,
                    OwnerUserId: owner,
                    DepartmentId: string.IsNullOrWhiteSpace(scenario) ? "department-default" : scenario,
                    LifecycleStatus: MapStatusToLifecycleLabel(status),
                    StageSummary: BuildStageSummary(status, hireId),
                    PrimarySignal: BuildPrimarySignal(status),
                    SignalLevel: status is "hired" or "interning_ai" ? "warn" : "ok",
                    OwningTeam: scenario,
                    CreatedAt: createdAt,
                    InternshipStartAt: status is "live" ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd") : null,
                    GraduatedAt: status is "live" ? DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd") : null,
                    TasksDone: status is "live" ? 8 + seedIndex : 0,
                    TasksTotal: status is "live" ? 20 : 0,
                    SatisfactionScore: status is "live" ? 4.6m : null,
                    PendingActions: BuildPendingActions(status),
                    Capabilities: capabilities,
                    EvalPhase: status switch
                    {
                        "hired" => "pending_materials",
                        "interning_ai" => "pending_materials",
                        "interning_human" => "pending_review",
                        _ => null
                    },
                    EvalIteration: status is "interning_human" ? 1 : null,
                    EvalMaxIterations: status is "interning_human" ? 30 : null,
                    IsConfigured: capabilities.All(item => item.Ready));

                employees.Add(employee);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"解析 fixture 目录失败：{directory}", ex);
            }
        }

        return new FixtureBundle(
            Employees: employees,
            FixtureDirectories: directories.Length);
    }

    /// <summary>
    /// 解析 Fixture 根目录。
    /// </summary>
    private static string? ResolveFixtureRoot()
    {
        return HireBotPathResolver.ResolveConventionalInstanceFixturesRoot();
    }

    /// <summary>
    /// 加载 Fixture 模板绑定。
    /// </summary>
    private static IReadOnlyDictionary<string, FixtureTemplateBinding> LoadFixtureTemplateBindings()
    {
        var fixtureRoot = ResolveFixtureRoot();
        if (string.IsNullOrWhiteSpace(fixtureRoot))
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }

        var bindingPath = Path.Combine(fixtureRoot, "template-bindings.json");
        if (!File.Exists(bindingPath))
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(bindingPath));
            var root = doc.RootElement;
            var items = new List<JsonElement>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetProperty("bindings", out var bindings) &&
                     bindings.ValueKind == JsonValueKind.Array)
            {
                items.AddRange(bindings.EnumerateArray());
            }

            var map = new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var templateId = TryGetString(item, "templateId");
                if (string.IsNullOrWhiteSpace(templateId))
                {
                    continue;
                }

                var fixtureTemplateId = TryGetString(item, "fixtureTemplateId");
                var fixtureEmployeeId = TryGetString(item, "fixtureEmployeeId");
                if (string.IsNullOrWhiteSpace(fixtureTemplateId) && string.IsNullOrWhiteSpace(fixtureEmployeeId))
                {
                    continue;
                }

                map[templateId.Trim()] = new FixtureTemplateBinding(
                    TemplateId: templateId.Trim(),
                    FixtureTemplateId: string.IsNullOrWhiteSpace(fixtureTemplateId) ? null : fixtureTemplateId.Trim(),
                    FixtureEmployeeId: string.IsNullOrWhiteSpace(fixtureEmployeeId) ? null : fixtureEmployeeId.Trim());
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, FixtureTemplateBinding>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 解析 Fixture 模板绑定。
    /// </summary>
    private static FixtureTemplateBinding? ResolveFixtureTemplateBinding(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        return FixtureTemplateBindings.Value.TryGetValue(templateId.Trim(), out var binding)
            ? binding
            : null;
    }

    /// <summary>
    /// 判断 Fixture 模板是否匹配。
    /// </summary>
    private static bool IsFixtureTemplateMatch(
        EmployeeDetailDto employee,
        string requestedTemplateId,
        FixtureTemplateBinding? fixtureBinding)
    {
        if (fixtureBinding is not null)
        {
            if (!string.IsNullOrWhiteSpace(fixtureBinding.FixtureEmployeeId) &&
                string.Equals(employee.EmployeeId, fixtureBinding.FixtureEmployeeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(fixtureBinding.FixtureTemplateId) &&
                (string.Equals(employee.BasedOnTemplateId, fixtureBinding.FixtureTemplateId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(employee.SourceTemplateId, fixtureBinding.FixtureTemplateId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return string.Equals(employee.BasedOnTemplateId, requestedTemplateId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(employee.SourceTemplateId, requestedTemplateId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析 Fixture 种子状态。
    /// </summary>
    private static string ResolveFixtureSeedStatus(int index)
    {
        if (FixtureStatusSeedOrder.Length == 0)
        {
            return "hired";
        }

        return FixtureStatusSeedOrder[index % FixtureStatusSeedOrder.Length];
    }

    /// <summary>
    /// 构建 Fixture 昵称。
    /// </summary>
    private static string BuildFixtureNickname(string displayName, string employeeId)
    {
        var suffix = employeeId.Split('_').LastOrDefault() ?? "seed";
        return $"{displayName}-{suffix}";
    }

    /// <summary>
    /// 解析创建时间。
    /// </summary>
    private static DateTimeOffset ResolveCreatedAt(string generatedAtUtc, int seedOffset)
    {
        if (DateTimeOffset.TryParse(generatedAtUtc, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTimeOffset.UtcNow.AddDays(-seedOffset);
    }

    /// <summary>
    /// 构建阶段摘要。
    /// </summary>
    private static string BuildStageSummary(string status, string hireId)
    {
        return status switch
        {
            "hired" => $"实例包 {hireId} 已生成，等待发起评估",
            "interning_ai" => "实例已准备完成，等待 AI 评估执行",
            "interning_human" => "AI 评估已通过，等待人工复核",
            "live" => "已上岗，稳定参与团队协作",
            "failed" => "评估未通过，等待回退处理",
            "retired" => "已退役，等待归档",
            _ => "状态更新中"
        };
    }

    /// <summary>
    /// 构建主要信号。
    /// </summary>
    private static string BuildPrimarySignal(string status)
    {
        return status switch
        {
            "hired" => "待操作：发起评估",
            "interning_ai" => "待执行 AI 评估",
            "interning_human" => "待人工审核",
            "live" => "运行稳定",
            "failed" => "评估未通过",
            "retired" => "实例已退役",
            _ => "状态同步中"
        };
    }

    /// <summary>
    /// 构建待办操作列表。
    /// </summary>
    private static string[] BuildPendingActions(string status)
    {
        return status switch
        {
            "hired" => ["确认团队归属", "检查技能配置"],
            "interning_ai" => ["准备评估材料", "确认评估场景"],
            "interning_human" => ["执行人工复核"],
            "live" => ["跟踪运行指标"],
            _ => []
        };
    }

    /// <summary>
    /// 尝试获取 JSON 字符串值。
    /// </summary>
    private static string TryGetString(JsonElement element, string propertyName, string fallback = "")
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? fallback,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }

    /// <summary>
    /// 检查 owner 下是否已存在同昵称的 personal_clone / private_branch。
    /// 昵称存储在 RuntimeSnapshotJson 中，无专用列，需反序列化判断。
    /// </summary>
    private async Task<bool> NicknameExistsForOwnerAsync(string owner, string nickname, CancellationToken cancellationToken)
    {
        var instances = await dbContext.Instances
            .AsNoTracking()
            .Where(item => item.OwnerUserId == owner &&
                           (item.InstanceType == "personal_clone" || item.InstanceType == "private_branch"))
            .Select(item => item.RuntimeSnapshotJson)
            .ToArrayAsync(cancellationToken);

        foreach (var json in instances)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            var dto = DeserializeEmployeeSnapshot(json);
            if (dto is not null && string.Equals(dto.Nickname, nickname, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 转换为摘要。
    /// </summary>
    private static EmployeeSummaryDto ToSummary(EmployeeDetailDto detail)
    {
        var status = NormalizeStatus(detail.Status, detail.LifecycleStatus) ?? "hired";
        var lifecycleStatus = string.IsNullOrWhiteSpace(detail.LifecycleStatus)
            ? MapStatusToLifecycleLabel(status)
            : detail.LifecycleStatus;
        return new EmployeeSummaryDto(
            detail.EmployeeId,
            detail.Nickname,
            detail.RoleName,
            detail.SourceTemplate,
            detail.SourceTemplateId,
            string.IsNullOrWhiteSpace(detail.InstanceType) ? "department" : detail.InstanceType,
            status,
            detail.BasedOnTemplateId,
            detail.FromInstanceId,
            string.IsNullOrWhiteSpace(detail.OwnerUserId) ? "unknown" : detail.OwnerUserId,
            string.IsNullOrWhiteSpace(detail.DepartmentId) ? "department-default" : detail.DepartmentId,
            lifecycleStatus,
            detail.StageSummary,
            detail.PrimarySignal,
            detail.SignalLevel,
            detail.OwningTeam,
            detail.CreatedAt,
            detail.TasksDone,
            detail.TasksTotal,
            detail.PendingActions,
            detail.IsConfigured,
            detail.CardIntro,
            detail.Description,
            detail.CreatedBy);
    }

}
