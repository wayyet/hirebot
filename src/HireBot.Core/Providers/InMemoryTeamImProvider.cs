using System.Collections.Concurrent;
using System.Globalization;
using HireBot.Abstraction.Models.Team;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

public sealed class InMemoryTeamImProvider : ITeamImProvider
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    private sealed class TeamImItemState
    {
        public required string ItemId { get; init; }
        public required string EmployeeId { get; init; }
        public required string EmployeeName { get; init; }
        public required string Category { get; init; }
        public required string Content { get; init; }
        public required string Source { get; init; }
        public required DateTime ReceivedAtUtc { get; init; }
        public string Status { get; set; } = "pending";
        public DateTime? ConfirmedAtUtc { get; set; }
        public HashSet<string> RequestIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public object SyncRoot { get; } = new();
    }

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TeamImItemState>> itemsByOwner =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<TeamImItemDto>> GetItemsAsync(
        string ownerSubject,
        TeamImQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var bucket = EnsureBucket(ownerSubject);
        var status = (query.Status ?? "pending").Trim().ToLowerInvariant();
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);

        var filtered = bucket.Values
            .Where(item => string.IsNullOrWhiteSpace(query.EmployeeId) || item.EmployeeId.Equals(query.EmployeeId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(query.Category) || item.Category.Equals(query.Category.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(query.Source) || item.Source.Contains(query.Source.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(item => item.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        var data = filtered
            .OrderByDescending(item => item.ReceivedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToArray();

        return Task.FromResult<IReadOnlyList<TeamImItemDto>>(data);
    }

    public Task<TeamImItemDto?> ConfirmItemAsync(
        string ownerSubject,
        string itemId,
        string? requestId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var _ = actor;
        var bucket = EnsureBucket(ownerSubject);
        if (!bucket.TryGetValue(itemId, out var item))
        {
            return Task.FromResult<TeamImItemDto?>(null);
        }

        var normalizedRequestId = string.IsNullOrWhiteSpace(requestId) ? null : requestId.Trim();
        lock (item.SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(normalizedRequestId) && item.RequestIds.Contains(normalizedRequestId))
            {
                return Task.FromResult<TeamImItemDto?>(ToDto(item));
            }

            if (!item.Status.Equals("confirmed", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = "confirmed";
                item.ConfirmedAtUtc = DateTime.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(normalizedRequestId))
            {
                item.RequestIds.Add(normalizedRequestId);
            }

            return Task.FromResult<TeamImItemDto?>(ToDto(item));
        }
    }

    public Task<int> ReplaceItemsAsync(
        string ownerSubject,
        IReadOnlyList<TeamImItemDto> items,
        CancellationToken cancellationToken = default)
    {
        var bucket = new ConcurrentDictionary<string, TeamImItemState>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemId))
            {
                continue;
            }

            var state = ToState(item);
            bucket[state.ItemId] = state;
        }

        itemsByOwner[ownerSubject] = bucket;
        return Task.FromResult(bucket.Count);
    }

    private ConcurrentDictionary<string, TeamImItemState> EnsureBucket(string ownerSubject)
    {
        return itemsByOwner.GetOrAdd(
            ownerSubject,
            _ => new ConcurrentDictionary<string, TeamImItemState>(StringComparer.OrdinalIgnoreCase));
    }

    private static TeamImItemState ToState(TeamImItemDto dto)
    {
        var receivedAtUtc = ParseTimestamp(dto.ReceivedAt) ?? DateTime.UtcNow;
        var confirmedAtUtc = ParseTimestamp(dto.ConfirmedAt);
        var status = string.Equals(dto.Status, "confirmed", StringComparison.OrdinalIgnoreCase) || confirmedAtUtc.HasValue
            ? "confirmed"
            : "pending";

        return new TeamImItemState
        {
            ItemId = dto.ItemId.Trim(),
            EmployeeId = dto.EmployeeId.Trim(),
            EmployeeName = dto.EmployeeName,
            Category = dto.Category,
            Content = dto.Content,
            Source = dto.Source,
            ReceivedAtUtc = receivedAtUtc,
            Status = status,
            ConfirmedAtUtc = status == "confirmed" ? confirmedAtUtc ?? DateTime.UtcNow : null
        };
    }

    private static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static TeamImItemDto ToDto(TeamImItemState state)
    {
        return new TeamImItemDto(
            state.ItemId,
            state.EmployeeId,
            state.EmployeeName,
            state.Category,
            state.Content,
            state.Source,
            state.ReceivedAtUtc.ToLocalTime().ToString(TimestampFormat),
            state.Status,
            state.ConfirmedAtUtc?.ToLocalTime().ToString(TimestampFormat));
    }
}
