using System.ComponentModel.DataAnnotations;

namespace HireBot.Repository.Entities;

public sealed class EvaluationWorkspaceStateEntity
{
    [Required]
    [MaxLength(120)]
    public string OwnerSubject { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string EmployeeId { get; set; } = string.Empty;

    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}