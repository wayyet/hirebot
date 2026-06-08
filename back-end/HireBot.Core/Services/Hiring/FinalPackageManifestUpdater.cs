using System.Text.Json;
using System.Text.Json.Nodes;
using HireBot.Abstraction.Models.Hiring;

namespace HireBot.Core.Services.Hiring;

internal static class FinalPackageManifestUpdater
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static FinalPackageManifestUpdateResult AppendLinkedSkills(
        IDictionary<string, byte[]> packageFiles,
        HiringSkillLinkConfigDto config)
    {
        ArgumentNullException.ThrowIfNull(packageFiles);
        ArgumentNullException.ThrowIfNull(config);

        var requestedSkills = config.LinkedSkills.Count;
        if (!packageFiles.TryGetValue("manifest.json", out var manifestBytes) ||
            manifestBytes.Length == 0)
        {
            return new FinalPackageManifestUpdateResult(
                ManifestFound: false,
                Updated: false,
                RequestedSkillCount: requestedSkills,
                ExistingSkillCount: 0,
                AddedSkillCount: 0,
                FinalSkillCount: 0,
                AddedSkillPaths: []);
        }

        if (requestedSkills == 0)
        {
            var existingSkillCount = TryReadSkillCount(manifestBytes);
            return new FinalPackageManifestUpdateResult(
                ManifestFound: true,
                Updated: false,
                RequestedSkillCount: 0,
                ExistingSkillCount: existingSkillCount,
                AddedSkillCount: 0,
                FinalSkillCount: existingSkillCount,
                AddedSkillPaths: []);
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(manifestBytes);
        }
        catch (JsonException)
        {
            return new FinalPackageManifestUpdateResult(
                ManifestFound: true,
                Updated: false,
                RequestedSkillCount: requestedSkills,
                ExistingSkillCount: 0,
                AddedSkillCount: 0,
                FinalSkillCount: 0,
                AddedSkillPaths: []);
        }

        if (rootNode is not JsonObject manifestObject)
        {
            return new FinalPackageManifestUpdateResult(
                ManifestFound: true,
                Updated: false,
                RequestedSkillCount: requestedSkills,
                ExistingSkillCount: 0,
                AddedSkillCount: 0,
                FinalSkillCount: 0,
                AddedSkillPaths: []);
        }

        var skillsArray = manifestObject["skills"] as JsonArray;
        if (skillsArray is null)
        {
            skillsArray = [];
            manifestObject["skills"] = skillsArray;
        }

        var manifestSkillCount = skillsArray.Count;
        var addedSkillPaths = new List<string>();
        foreach (var linkedSkill in config.LinkedSkills)
        {
            var skillName = linkedSkill.Name?.Trim();
            if (string.IsNullOrWhiteSpace(skillName))
            {
                skillName = linkedSkill.SkillId?.Trim();
            }

            if (string.IsNullOrWhiteSpace(skillName))
            {
                continue;
            }

            var skillSlug = SanitizeSkillSlug(skillName);
            var skillPath = $"skills/{skillSlug}/SKILL.md";
            if (ContainsSkill(skillsArray, skillName, skillPath))
            {
                continue;
            }

            skillsArray.Add(new JsonObject
            {
                ["name"] = skillName,
                ["path"] = skillPath,
                ["required"] = true
            });
            addedSkillPaths.Add(skillPath);
        }

        if (addedSkillPaths.Count == 0)
        {
            return new FinalPackageManifestUpdateResult(
                ManifestFound: true,
                Updated: false,
                RequestedSkillCount: requestedSkills,
                ExistingSkillCount: manifestSkillCount,
                AddedSkillCount: 0,
                FinalSkillCount: skillsArray.Count,
                AddedSkillPaths: []);
        }

        packageFiles["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifestObject, SerializerOptions);
        return new FinalPackageManifestUpdateResult(
            ManifestFound: true,
            Updated: true,
            RequestedSkillCount: requestedSkills,
            ExistingSkillCount: manifestSkillCount,
            AddedSkillCount: addedSkillPaths.Count,
            FinalSkillCount: skillsArray.Count,
            AddedSkillPaths: addedSkillPaths);
    }

    private static bool ContainsSkill(JsonArray skillsArray, string skillName, string skillPath)
    {
        foreach (var node in skillsArray)
        {
            if (node is not JsonObject skillObject)
            {
                continue;
            }

            var existingName = skillObject["name"]?.GetValue<string>()?.Trim();
            var existingPath = skillObject["path"]?.GetValue<string>()?.Trim();
            if (string.Equals(existingName, skillName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(existingPath, skillPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string SanitizeSkillSlug(string raw)
    {
        var chars = raw.Select(ch =>
            char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.'
                ? ch
                : '-').ToArray();
        var slug = new string(chars).Trim('-', '.');
        return string.IsNullOrWhiteSpace(slug) ? "skill" : slug;
    }

    private static int TryReadSkillCount(byte[] manifestBytes)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestBytes);
            if (!document.RootElement.TryGetProperty("skills", out var skillsElement) ||
                skillsElement.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            return skillsElement.GetArrayLength();
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}

internal sealed record FinalPackageManifestUpdateResult(
    bool ManifestFound,
    bool Updated,
    int RequestedSkillCount,
    int ExistingSkillCount,
    int AddedSkillCount,
    int FinalSkillCount,
    IReadOnlyList<string> AddedSkillPaths);
