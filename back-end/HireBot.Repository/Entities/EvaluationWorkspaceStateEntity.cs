using System.ComponentModel.DataAnnotations;
using HireBot.Abstraction.Contracts;

namespace HireBot.Repository.Entities;

public sealed class EvaluationWorkspaceStateEntity : ITenant
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(120)]
    public string OwnerSubject { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? TenantId { get; set; }

    [Required]
    [MaxLength(120)]
    public string EmployeeId { get; set; } = string.Empty;

    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}