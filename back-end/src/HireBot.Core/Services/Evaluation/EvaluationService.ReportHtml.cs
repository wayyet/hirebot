using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeRuntime;
using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Evaluation.Tools;
using HireBot.Abstraction.Models.Hiring;
using HireBot.Abstraction.Models.Sandbox;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.Evaluation;
using HireBot.Abstraction.Services.Hiring;
using HireBot.Abstraction.Services.Sandbox;
using HireBot.Core.Services.Evaluation.Persistence;
using HireBot.Core.Services.Internal;
using HireBot.Core.Services.Sandbox;
using HireBot.Core.Services.SystemSkills;
using HireBot.Repository;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.Evaluation;

internal sealed partial class EvaluationService
{
    private static string BuildReportHtml(object payload, IReadOnlyList<EvaluationDimensionScoreDto> dimensionScores)
    {
        static string LocalizeDimensionName(string dimension)
        {
            return dimension.Trim().ToLowerInvariant() switch
            {
                "accuracy" => "准确性",
                "completeness" => "完整性",
                "compliance" => "合规性",
                "communication" => "沟通质量",
                _ => string.IsNullOrWhiteSpace(dimension) ? "未命名维度" : dimension.Trim()
            };
        }

        static string ScoreLevel(decimal score)
        {
            return score switch
            {
                >= 85m => "优秀",
                >= 70m => "良好",
                >= 60m => "合格",
                _ => "待改进"
            };
        }

        static string ScoreColor(decimal score)
        {
            return score switch
            {
                >= 85m => "#10b981",
                >= 70m => "#3b82f6",
                >= 60m => "#f59e0b",
                _ => "#ef4444"
            };
        }

        var payloadElement = JsonSerializer.SerializeToElement(payload, JsonOptions);
        string? summary = null;
        string? generatedAtUtc = null;
        string? employeeId = null;
        string? sessionId = null;
        int? iteration = null;
        decimal? overallScore = null;
        bool? passed = null;

        if (payloadElement.ValueKind == JsonValueKind.Object)
        {
            if (payloadElement.TryGetProperty("summary", out var summaryProperty) &&
                summaryProperty.ValueKind == JsonValueKind.String)
            {
                summary = summaryProperty.GetString();
            }

            if (payloadElement.TryGetProperty("generatedAtUtc", out var generatedAtProperty) &&
                generatedAtProperty.ValueKind == JsonValueKind.String)
            {
                generatedAtUtc = generatedAtProperty.GetString();
            }

            if (payloadElement.TryGetProperty("employeeId", out var employeeProperty) &&
                employeeProperty.ValueKind == JsonValueKind.String)
            {
                employeeId = employeeProperty.GetString();
            }

            if (payloadElement.TryGetProperty("sessionId", out var sessionProperty) &&
                sessionProperty.ValueKind == JsonValueKind.String)
            {
                sessionId = sessionProperty.GetString();
            }

            if (payloadElement.TryGetProperty("iteration", out var iterationProperty) &&
                iterationProperty.ValueKind is JsonValueKind.Number &&
                iterationProperty.TryGetInt32(out var parsedIteration))
            {
                iteration = parsedIteration;
            }

            if (payloadElement.TryGetProperty("overallScore", out var scoreProperty) &&
                scoreProperty.ValueKind is JsonValueKind.Number &&
                scoreProperty.TryGetDecimal(out var parsedScore))
            {
                overallScore = parsedScore;
            }

            if (payloadElement.TryGetProperty("passed", out var passedProperty) &&
                passedProperty.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                passed = passedProperty.GetBoolean();
            }
        }

        var localizedSummary = string.IsNullOrWhiteSpace(summary)
            ? passed == true
                ? "本轮评估达到通过标准，可进入人工审核流程。"
                : passed == false
                    ? "本轮评估未达到通过标准，建议根据维度得分优化后重试。"
                    : "评估已完成，等待后续决策。"
            : summary.Trim();
        if (localizedSummary.StartsWith("Auto-evaluation passed", StringComparison.OrdinalIgnoreCase))
        {
            localizedSummary = $"自动评估完成，综合评分 {overallScore?.ToString("0.##") ?? "—"}，判定通过。";
        }
        else if (localizedSummary.StartsWith("Auto-evaluation failed", StringComparison.OrdinalIgnoreCase))
        {
            localizedSummary = $"自动评估完成，综合评分 {overallScore?.ToString("0.##") ?? "—"}，判定未通过。";
        }

        var generatedAtDisplay = DateTimeOffset.TryParse(generatedAtUtc, out var parsedGeneratedAt)
            ? parsedGeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "—";
        var scoreDisplay = overallScore?.ToString("0.##") ?? "—";
        var iterationDisplay = iteration.HasValue ? $"第 {iteration.Value} 轮" : "—";
        var statusClass = passed == true ? "status-pass" : passed == false ? "status-fail" : "status-pending";
        var statusText = passed == true ? "评估通过" : passed == false ? "评估未通过" : "评估进行中";

        var rows = dimensionScores.Count == 0
            ? "<tr><td colspan=\"5\" class=\"empty\">暂无维度评分数据</td></tr>"
            : string.Join(
                Environment.NewLine,
                dimensionScores.Select(item =>
                {
                    var score = Math.Round(Math.Clamp(item.Score, 0m, 100m), 2);
                    var width = score.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    var level = ScoreLevel(score);
                    var color = ScoreColor(score);
                    return $"""
                            <tr>
                                <td>{System.Net.WebUtility.HtmlEncode(LocalizeDimensionName(item.Dimension))}</td>
                                <td class="score">{score:0.##}</td>
                                <td>
                                    <div class="bar-track">
                                        <span class="bar-fill" style="width: {width}%; background: {color};"></span>
                                    </div>
                                </td>
                                <td>{System.Net.WebUtility.HtmlEncode(level)}</td>
                                <td>{System.Net.WebUtility.HtmlEncode(item.Comment)}</td>
                            </tr>
                            """;
                }));
        var payloadJson = System.Net.WebUtility.HtmlEncode(JsonSerializer.Serialize(payload, JsonOptions));

        return $$"""
                 <!doctype html>
                 <html lang="zh-CN">
                 <head>
                     <meta charset="utf-8" />
                     <meta name="viewport" content="width=device-width, initial-scale=1" />
                     <title>AI 评估报告</title>
                     <style>
                         :root {
                             --text-main: #0f172a;
                             --text-muted: #6b7280;
                             --line: #e5e7eb;
                             --surface: #ffffff;
                             --surface-soft: #f8fafc;
                             --accent: #4f46e5;
                         }
                         * { box-sizing: border-box; }
                         body {
                             margin: 0;
                             padding: 32px 20px;
                             background: linear-gradient(180deg, #fdf2f8 0%, #eef2ff 100%);
                             color: var(--text-main);
                             font-family: "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif;
                         }
                         .report {
                             max-width: 1080px;
                             margin: 0 auto;
                             background: var(--surface);
                             border: 1px solid var(--line);
                             border-radius: 20px;
                             overflow: hidden;
                             box-shadow: 0 18px 36px rgba(15, 23, 42, 0.08);
                         }
                         .header {
                             padding: 22px 24px 18px;
                             background: linear-gradient(120deg, #eef2ff 0%, #f8fafc 100%);
                             border-bottom: 1px solid var(--line);
                         }
                         h1 {
                             margin: 0;
                             font-size: 24px;
                             letter-spacing: 0.2px;
                         }
                         .subline {
                             margin-top: 6px;
                             color: var(--text-muted);
                             font-size: 13px;
                         }
                         .meta-grid {
                             margin-top: 16px;
                             display: grid;
                             gap: 10px;
                             grid-template-columns: repeat(4, minmax(0, 1fr));
                         }
                         .meta-card {
                             border: 1px solid var(--line);
                             border-radius: 14px;
                             background: var(--surface);
                             padding: 10px 12px;
                         }
                         .meta-label {
                             color: var(--text-muted);
                             font-size: 12px;
                         }
                         .meta-value {
                             margin-top: 4px;
                             font-size: 16px;
                             font-weight: 700;
                         }
                         .status-chip {
                             display: inline-flex;
                             align-items: center;
                             gap: 6px;
                             border-radius: 999px;
                             padding: 4px 10px;
                             font-size: 12px;
                             font-weight: 600;
                         }
                         .status-pass { color: #047857; background: #ecfdf5; border: 1px solid #a7f3d0; }
                         .status-fail { color: #b91c1c; background: #fef2f2; border: 1px solid #fecaca; }
                         .status-pending { color: #92400e; background: #fffbeb; border: 1px solid #fde68a; }
                         .section {
                             padding: 18px 24px;
                             border-bottom: 1px solid var(--line);
                         }
                         .section:last-child { border-bottom: none; }
                         h2 {
                             margin: 0 0 12px 0;
                             font-size: 17px;
                         }
                         .summary {
                             margin: 0;
                             border: 1px solid #dbeafe;
                             background: #eff6ff;
                             color: #1e3a8a;
                             border-radius: 12px;
                             padding: 10px 12px;
                             font-size: 14px;
                             line-height: 1.6;
                         }
                         table {
                             width: 100%;
                             border-collapse: collapse;
                             border: 1px solid var(--line);
                             border-radius: 14px;
                             overflow: hidden;
                             background: var(--surface);
                         }
                         th, td {
                             border-bottom: 1px solid var(--line);
                             padding: 10px 12px;
                             text-align: left;
                             vertical-align: middle;
                             font-size: 13px;
                         }
                         th {
                             background: var(--surface-soft);
                             color: var(--text-muted);
                             font-weight: 600;
                             font-size: 12px;
                         }
                         td.score {
                             font-weight: 700;
                             font-variant-numeric: tabular-nums;
                         }
                         .bar-track {
                             width: 120px;
                             height: 8px;
                             background: #e5e7eb;
                             border-radius: 999px;
                             overflow: hidden;
                         }
                         .bar-fill {
                             display: block;
                             height: 100%;
                             border-radius: 999px;
                         }
                         .empty {
                             text-align: center;
                             color: var(--text-muted);
                             padding: 16px;
                         }
                         pre {
                             margin: 0;
                             white-space: pre-wrap;
                             word-break: break-word;
                             border: 1px solid var(--line);
                             border-radius: 12px;
                             background: #f9fafb;
                             color: #1f2937;
                             padding: 12px 14px;
                             font-size: 12px;
                             line-height: 1.55;
                         }
                         @media (max-width: 900px) {
                             .meta-grid {
                                 grid-template-columns: repeat(2, minmax(0, 1fr));
                             }
                         }
                         @media (max-width: 560px) {
                             body { padding: 14px 10px; }
                             .header, .section { padding: 14px; }
                             .meta-grid { grid-template-columns: 1fr; }
                             .bar-track { width: 88px; }
                         }
                     </style>
                 </head>
                 <body>
                     <article class="report">
                         <header class="header">
                             <h1>AI 评估报告</h1>
                             <div class="subline">生成时间：{{System.Net.WebUtility.HtmlEncode(generatedAtDisplay)}}</div>
                             <div class="meta-grid">
                                 <div class="meta-card">
                                     <div class="meta-label">轮次</div>
                                     <div class="meta-value">{{System.Net.WebUtility.HtmlEncode(iterationDisplay)}}</div>
                                 </div>
                                 <div class="meta-card">
                                     <div class="meta-label">综合评分</div>
                                     <div class="meta-value">{{System.Net.WebUtility.HtmlEncode(scoreDisplay)}}</div>
                                 </div>
                                 <div class="meta-card">
                                     <div class="meta-label">员工 ID</div>
                                     <div class="meta-value" style="font-size: 12px; font-weight: 600;">{{System.Net.WebUtility.HtmlEncode(employeeId ?? "—")}}</div>
                                 </div>
                                 <div class="meta-card">
                                     <div class="meta-label">会话 ID</div>
                                     <div class="meta-value" style="font-size: 12px; font-weight: 600;">{{System.Net.WebUtility.HtmlEncode(sessionId ?? "—")}}</div>
                                 </div>
                             </div>
                             <div style="margin-top: 12px;">
                                 <span class="status-chip {{statusClass}}">{{System.Net.WebUtility.HtmlEncode(statusText)}}</span>
                             </div>
                         </header>

                         <section class="section">
                             <h2>评估摘要</h2>
                             <p class="summary">{{System.Net.WebUtility.HtmlEncode(localizedSummary)}}</p>
                         </section>

                         <section class="section">
                             <h2>维度评分</h2>
                             <table>
                                 <thead>
                                     <tr>
                                         <th>维度</th>
                                         <th>分数</th>
                                         <th>进度条</th>
                                         <th>等级</th>
                                         <th>说明</th>
                                     </tr>
                                 </thead>
                                 <tbody>
                                 {{rows}}
                                 </tbody>
                             </table>
                         </section>

                         <section class="section">
                             <h2>原始数据（JSON）</h2>
                             <pre>{{payloadJson}}</pre>
                         </section>
                     </article>
                 </body>
                 </html>
                 """;
    }

}
