using HireBot.Abstraction.Models.Evaluation;
using HireBot.Abstraction.Models.Training;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

public sealed class MockEvaluationScenarioProvider : IEvaluationScenarioProvider
{
    public Task<TrainingStateDto> GetTrainingStateAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        var score = ComputeScore(employeeId);
        var aiPassed = score >= 75m;

        var checkpoints = new List<TrainingCheckpointDto>
        {
            new("materials", "材料准备", "DONE", "已上传训练材料"),
            new("exam", "模拟考试", aiPassed ? "DONE" : "NEEDS_IMPROVEMENT", $"得分 {score:0}"),
            new("report", "评估报告", "DONE", aiPassed ? "达到通过线" : "建议继续进化")
        };

        var dto = new TrainingStateDto(
            EmployeeId: employeeId,
            Phase: aiPassed ? "decision" : "evolution",
            EvolutionRound: aiPassed ? 1 : 2,
            ExamScore: score,
            AiPassed: aiPassed,
            RequiresHumanReview: true,
            Checkpoints: checkpoints);

        return Task.FromResult(dto);
    }

    public Task<EvaluationStateDto> GetEvaluationStateAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        var score = ComputeScore(employeeId);
        var allPassed = score >= 80m;

        var scenarios = new List<EvaluationScenarioDto>
        {
            new("HS-001", "投诉处理", "completed", "passed", "语气友好，流程完整", 6, "14:00", "14:15"),
            new("HS-002", "功能建议", "completed", allPassed ? "passed" : "failed", allPassed ? "记录及时" : "跟进承诺不够明确", 5, "14:16", "14:28"),
            new("HS-003", "售后咨询", "completed", allPassed ? "passed" : "failed", allPassed ? "信息准确" : "回答不够具体", 5, "14:28", "14:40")
        };

        var state = new EvaluationStateDto(
            EmployeeId: employeeId,
            OverallStatus: allPassed ? "passed" : "pending_review",
            Scenarios: scenarios,
            Recommendation: allPassed ? "建议确认上岗" : "建议补充配置后复评");

        return Task.FromResult(state);
    }

    private static decimal ComputeScore(string employeeId)
    {
        var seed = employeeId
            .Where(char.IsLetterOrDigit)
            .Select(ch => (int)ch)
            .DefaultIfEmpty(65)
            .Sum();

        return 60m + seed % 41;
    }
}
