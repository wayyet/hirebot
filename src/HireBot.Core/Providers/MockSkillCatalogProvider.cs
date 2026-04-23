using HireBot.Abstraction.Models.SkillCatalog;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

public sealed class MockSkillCatalogProvider : ISkillCatalogProvider
{
    private static readonly IReadOnlyList<SkillDetailDto> Skills =
    [
        new("s001", "意图识别", "识别用户请求意图并输出标准标签", "L1", "上架中", "v1.2.0", "2026-03-10", "{\"text\":\"我要退款\"}", "{\"intent\":\"refund\",\"confidence\":0.95}", ["nlp", "intent"], ["t002", "t005"], ["intent.md", "rules.json"]),
        new("s002", "FAQ 检索", "在企业知识库中检索可解释答案", "L2", "上架中", "v2.0.1", "2026-03-01", "{\"question\":\"如何开发票\"}", "{\"answer\":\"...\",\"sources\":[\"faq-01\"]}", ["rag", "faq"], ["t002"], ["faq-index.json"]),
        new("s003", "风险条款比对", "合同条款与企业红线库比对并标注风险", "L3", "上架中", "v0.9.4", "2026-02-14", "{\"clause\":\"...\"}", "{\"risk\":\"high\",\"reason\":\"...\"}", ["legal", "compliance"], ["t003"], ["risk-rules.yaml"]),
        new("s004", "线索优先级评分", "根据商机信号和历史转化率给线索打分", "L2", "下架中", "v1.4.3", "2026-01-19", "{\"lead\":{...}}", "{\"score\":87}", ["sales", "scoring"], ["t001"], ["scoring-model.bin"])
    ];

    public Task<IReadOnlyList<SkillSummaryDto>> GetSkillsAsync(string? q, string? level, string? status, CancellationToken cancellationToken = default)
    {
        var filtered = Skills
            .Where(item => string.IsNullOrWhiteSpace(q)
                           || item.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
                           || item.Description.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(level)
                           || item.Level.Equals(level.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(status)
                           || item.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(item => new SkillSummaryDto(
                item.SkillId,
                item.Name,
                item.Description,
                item.Level,
                item.Status,
                item.Version,
                item.UpdatedAt))
            .ToArray();

        return Task.FromResult<IReadOnlyList<SkillSummaryDto>>(filtered);
    }

    public Task<SkillDetailDto?> GetSkillAsync(string skillId, CancellationToken cancellationToken = default)
    {
        var skill = Skills.FirstOrDefault(item => item.SkillId.Equals(skillId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(skill);
    }
}
