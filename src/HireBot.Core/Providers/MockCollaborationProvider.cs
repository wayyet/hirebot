using System.Collections.Concurrent;
using HireBot.Abstraction.Models.Collaboration;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

public sealed class MockCollaborationProvider : ICollaborationProvider
{
    private readonly ConcurrentDictionary<string, HashSet<string>> archivedByOwner =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<CollaborationGroupSummaryDto>> GetGroupsAsync(
        string ownerSubject,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var archived = archivedByOwner.GetOrAdd(ownerSubject, _ => []);
        var groups = BuildBaseGroups()
            .Select(item => item with { IsArchived = archived.Contains(item.GroupId) })
            .Where(item => includeArchived || !item.IsArchived)
            .ToArray();

        return Task.FromResult<IReadOnlyList<CollaborationGroupSummaryDto>>(groups);
    }

    public Task<CollaborationGroupDetailDto?> GetGroupAsync(string ownerSubject, string groupId, CancellationToken cancellationToken = default)
    {
        var archived = archivedByOwner.GetOrAdd(ownerSubject, _ => []);
        var summary = BuildBaseGroups().FirstOrDefault(item => item.GroupId.Equals(groupId, StringComparison.OrdinalIgnoreCase));
        if (summary is null)
        {
            return Task.FromResult<CollaborationGroupDetailDto?>(null);
        }

        var detail = new CollaborationGroupDetailDto(
            summary.GroupId,
            summary.GroupName,
            summary.BusinessPurpose,
            summary.ImPlatform,
            summary.ImGroupId,
            summary.MemberCount,
            summary.DigitalEmployeeCount,
            summary.RecentActivityTime,
            summary.CollaborationVolume7d,
            summary.Status,
            summary.PrimarySignal,
            archived.Contains(summary.GroupId),
            BuildMembers(summary.GroupId));

        return Task.FromResult<CollaborationGroupDetailDto?>(detail);
    }

    public async Task<CollaborationGroupDetailDto?> SetArchivedAsync(string ownerSubject, string groupId, bool archived, CancellationToken cancellationToken = default)
    {
        var bucket = archivedByOwner.GetOrAdd(ownerSubject, _ => []);
        lock (bucket)
        {
            if (archived)
            {
                bucket.Add(groupId);
            }
            else
            {
                bucket.Remove(groupId);
            }
        }

        return await GetGroupAsync(ownerSubject, groupId, cancellationToken);
    }

    public Task<int> MarkArchivedAsync(string ownerSubject, IReadOnlyList<string> groupIds, CancellationToken cancellationToken = default)
    {
        if (groupIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        var bucket = archivedByOwner.GetOrAdd(ownerSubject, _ => []);
        var affected = 0;
        lock (bucket)
        {
            foreach (var groupId in groupIds.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                if (bucket.Add(groupId.Trim()))
                {
                    affected++;
                }
            }
        }

        return Task.FromResult(affected);
    }

    private static IReadOnlyList<CollaborationGroupSummaryDto> BuildBaseGroups()
    {
        return
        [
            new("g001", "销售作战群", "销售线索跟进", "飞书", "oc_sales_001", 12, 3, "10 分钟前", 56, "活跃", "运行稳定", false),
            new("g002", "客户服务群", "售后工单协同", "企业微信", "wx_cs_002", 18, 2, "35 分钟前", 42, "低活跃", "响应偏慢", false),
            new("g003", "财务协作群", "对账与报表同步", "钉钉", "dd_fin_003", 9, 1, "昨天 18:20", 21, "异常", "权限告警待处理", false)
        ];
    }

    private static IReadOnlyList<CollaborationGroupMemberDto> BuildMembers(string groupId)
    {
        return groupId switch
        {
            "g001" =>
            [
                new("李娜", "销售主管", false, "2025-11-03", "10 分钟前"),
                new("销售跟进助理", "数字员工", true, "2025-11-12", "8 分钟前")
            ],
            "g002" =>
            [
                new("王强", "客服经理", false, "2025-10-13", "35 分钟前"),
                new("客服分流助手", "数字员工", true, "2025-12-08", "42 分钟前")
            ],
            _ =>
            [
                new("赵敏", "财务负责人", false, "2025-09-26", "昨天 18:20"),
                new("财务对账助手", "数字员工", true, "2025-12-20", "昨天 18:18")
            ]
        };
    }
}
