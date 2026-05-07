using System;

namespace HireBot.Core.Providers;

internal static class TemplatePresentationHelpers
{
    internal static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    internal static string BuildDefaultIconUrl(string templateId, string name)
    {
        var text = FirstNonEmpty(name, templateId).ToUpperInvariant();
        var firstGlyph = text.Length > 0 ? text[0].ToString() : "T";
        var background = ResolveColorFromTemplateId(templateId);
        var svg =
            "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160' viewBox='0 0 160 160'>" +
            $"<rect width='160' height='160' rx='24' fill='{background}' />" +
            $"<text x='80' y='97' text-anchor='middle' font-size='64' font-family='Arial' fill='white'>{firstGlyph}</text>" +
            "</svg>";
        var encoded = Uri.EscapeDataString(svg);
        return $"data:image/svg+xml,{encoded}";
    }

    internal static string ResolveColorFromTemplateId(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return "#334155";
        }

        var seed = 0;
        foreach (var ch in templateId)
        {
            seed += ch;
        }

        var palette = new[]
        {
            "#2563eb",
            "#0f766e",
            "#1d4ed8",
            "#0369a1",
            "#4f46e5",
            "#0891b2"
        };

        return palette[Math.Abs(seed) % palette.Length];
    }
}
