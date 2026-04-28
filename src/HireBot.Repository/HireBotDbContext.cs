using HireBot.Abstraction.Models.User;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Repository;

public sealed class HireBotDbContext(DbContextOptions<HireBotDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<EvaluationSessionEntity> EvaluationSessions { get; set; }
    public DbSet<EvaluationAssetEntity> EvaluationAssets { get; set; }
    public DbSet<EvaluationReportEntity> EvaluationReports { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(256);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt);

            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<EvaluationSessionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SessionId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.OwnerSubject).IsRequired().HasMaxLength(120);
            entity.Property(e => e.EmployeeId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.TargetHireId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.TargetSandboxId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.EvaluatorHireId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.EvaluatorSandboxId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(40);
            entity.Property(e => e.LastError).HasMaxLength(1024);
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            entity.HasIndex(e => e.SessionId).IsUnique();
            entity.HasIndex(e => new { e.OwnerSubject, e.EmployeeId, e.UpdatedAtUtc });
            entity.HasIndex(e => new { e.EvaluatorHireId, e.TargetHireId });
        });

        modelBuilder.Entity<EvaluationAssetEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AssetType).IsRequired().HasMaxLength(40);
            entity.Property(e => e.RelatedKey).HasMaxLength(160);
            entity.Property(e => e.RelativePath).IsRequired().HasMaxLength(512);
            entity.Property(e => e.PublicUrl).IsRequired().HasMaxLength(512);
            entity.Property(e => e.MimeType).IsRequired().HasMaxLength(120);
            entity.Property(e => e.ContentHash).IsRequired().HasMaxLength(128);
            entity.Property(e => e.SourceType).IsRequired().HasMaxLength(40);
            entity.Property(e => e.CreatedAtUtc).IsRequired();

            entity.HasIndex(e => new { e.SessionEntityId, e.AssetType });
            entity.HasIndex(e => e.RelativePath);

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Assets)
                .HasForeignKey(e => e.SessionEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EvaluationReportEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OverallScore).HasColumnType("numeric(6,2)");
            entity.Property(e => e.DimensionScoresJson).IsRequired();
            entity.Property(e => e.SummaryJson).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();

            entity.HasIndex(e => new { e.SessionEntityId, e.Iteration });

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Reports)
                .HasForeignKey(e => e.SessionEntityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<EvaluationAssetEntity>()
                .WithMany()
                .HasForeignKey(e => e.ReportJsonAssetId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<EvaluationAssetEntity>()
                .WithMany()
                .HasForeignKey(e => e.ReportHtmlAssetId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
