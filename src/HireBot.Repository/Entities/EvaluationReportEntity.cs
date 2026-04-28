using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HireBot.Repository.Entities;

public sealed class EvaluationReportEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid SessionEntityId { get; set; }

    [ForeignKey(nameof(SessionEntityId))]
    public EvaluationSessionEntity? Session { get; set; }

    public int Iteration { get; set; } = 1;

    [Column(TypeName = "numeric(6,2)")]
    public decimal OverallScore { get; set; }

    public bool Passed { get; set; }

    [Required]
    public string DimensionScoresJson { get; set; } = "{}";

    [Required]
    public string SummaryJson { get; set; } = "{}";

    public Guid? ReportJsonAssetId { get; set; }

    public Guid? ReportHtmlAssetId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
