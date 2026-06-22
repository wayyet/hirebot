using System.Security.Claims;
using System.Text.Json;

namespace HireBot.Core.Infrastructure.Identity;

public static class JwtRoleResolver
{
    public static HashSet<string> GetRoles(ClaimsPrincipal user, string resourceAccessClientId)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in ParseResourceAccessRoles(user, resourceAccessClientId))
        {
            roles.Add(role);
        }

        return roles;
    }

    private static IEnumerable<string> ParseResourceAccessRoles(ClaimsPrincipal user, string resourceAccessClientId)
    {
        if (string.IsNullOrWhiteSpace(resourceAccessClientId))
            return [];

        var results = new List<string>();

        foreach (var claim in user.Claims.Where(c => c.Type == "resource_access"))
        {
            if (string.IsNullOrWhiteSpace(claim.Value))
                continue;

            try
            {
                using var document = JsonDocument.Parse(claim.Value);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                if (root.TryGetProperty(resourceAccessClientId, out var clientNode))
                {
                    results.AddRange(ReadClientRoles(clientNode));
                }
            }
            catch
            {
                // Ignore malformed resource_access claim.
            }
        }

        return results;
    }

    private static IEnumerable<string> ReadClientRoles(JsonElement clientNode)
    {
        if (clientNode.ValueKind != JsonValueKind.Object ||
            !clientNode.TryGetProperty("roles", out var rolesNode) ||
            rolesNode.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var roleNode in rolesNode.EnumerateArray())
        {
            var role = roleNode.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(role))
            {
                yield return role;
            }
        }
    }
}
