using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class EvaluationSessionEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string SessionId { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string OwnerSubject { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string EmployeeId { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string TargetHireId { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string TargetSandboxId { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string EvaluatorHireId { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string EvaluatorSandboxId { get; set; } = string.Empty;

    [Required]
    [MaxLength(40)]
    public string Status { get; set; } = "ready";

    public int Iteration { get; set; } = 1;

    [MaxLength(1024)]
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<EvaluationAssetEntity> Assets { get; set; } = new List<EvaluationAssetEntity>();

    public ICollection<EvaluationReportEntity> Reports { get; set; } = new List<EvaluationReportEntity>();
}
