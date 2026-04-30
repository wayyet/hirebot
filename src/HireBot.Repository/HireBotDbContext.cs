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

    public DbSet<HiringSessionEntity> HiringSessions { get; set; }
    public DbSet<HiringRuntimeStateEntity> HiringRuntimeStates { get; set; }
    public DbSet<HiringCredentialBindingEntity> HiringCredentialBindings { get; set; }
    public DbSet<HiringArtifactEntity> HiringArtifacts { get; set; }
    public DbSet<HiringArtifactUploadEntity> HiringArtifactUploads { get; set; }
    public DbSet<HiringArtifactUploadPartEntity> HiringArtifactUploadParts { get; set; }
    public DbSet<HiringAuditLogEntity> HiringAuditLogs { get; set; }
    public DbSet<SandboxInstanceEntity> SandboxInstances { get; set; }
    public DbSet<SandboxSessionEntity> SandboxSessions { get; set; }
    public DbSet<SandboxAssetEntity> SandboxAssets { get; set; }

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

        modelBuilder.Entity<HiringSessionEntity>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.HasIndex(e => e.HireId).IsUnique();
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<HiringRuntimeStateEntity>(entity =>
        {
            entity.HasKey(e => e.HireId);
            entity.Property(e => e.SessionId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.HireId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CurrentStage).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CollectionPhase).IsRequired().HasMaxLength(64);
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.UpdatedAtUtc);
        });

        modelBuilder.Entity<HiringCredentialBindingEntity>(entity =>
        {
            entity.HasKey(e => e.BindingId);
            entity.Property(e => e.SessionId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.HireId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.CredentialSlot).IsRequired().HasMaxLength(160);
            entity.Property(e => e.SecretRef).HasMaxLength(256);
            entity.Property(e => e.AuthKind).HasMaxLength(80);
            entity.Property(e => e.TargetSystem).HasMaxLength(160);
            entity.Property(e => e.TodoId).HasMaxLength(160);
            entity.Property(e => e.BindingStatus).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ProtectedSecret).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            entity.HasIndex(e => new { e.SessionId, e.CredentialSlot }).IsUnique();
            entity.HasIndex(e => new { e.HireId, e.UpdatedAtUtc });
        });

        modelBuilder.Entity<HiringArtifactEntity>(entity =>
        {
            entity.HasKey(e => e.ArtifactId);
            entity.HasIndex(e => new { e.SessionId, e.Kind, e.LogicalPath }).IsUnique();
            entity.HasIndex(e => new { e.SessionId, e.IsFinal });
            entity.HasIndex(e => e.UploadedAtUtc);
        });

        modelBuilder.Entity<HiringArtifactUploadEntity>(entity =>
        {
            entity.HasKey(e => e.UploadId);
            entity.HasIndex(e => new { e.SessionId, e.Kind, e.LogicalPath, e.CompletedAtUtc });
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<HiringArtifactUploadPartEntity>(entity =>
        {
            entity.HasKey(e => e.PartId);
            entity.HasIndex(e => new { e.UploadId, e.PartNumber }).IsUnique();
        });

        modelBuilder.Entity<HiringAuditLogEntity>(entity =>
        {
            entity.HasKey(e => e.AuditId);
            entity.HasIndex(e => new { e.SessionId, e.TimestampUtc });
            entity.HasIndex(e => new { e.HireId, e.TimestampUtc });
        });

        modelBuilder.Entity<SandboxInstanceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SandboxId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.ScopeType).IsRequired().HasMaxLength(40);
            entity.Property(e => e.ScopeKey).IsRequired().HasMaxLength(160);
            entity.Property(e => e.SandboxRole).IsRequired().HasMaxLength(80);
            entity.Property(e => e.ProvisioningMode).IsRequired().HasMaxLength(40);
            entity.Property(e => e.OwnerSubject).IsRequired().HasMaxLength(256);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.OperatorId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.State).IsRequired().HasMaxLength(80);
            entity.Property(e => e.GatewayEndpoint).HasMaxLength(512);
            entity.Property(e => e.LastError).HasMaxLength(1024);
            entity.Property(e => e.UseCase).HasMaxLength(200);
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            entity.HasIndex(e => e.SandboxId).IsUnique();
            entity.HasIndex(e => new { e.OwnerSubject, e.ScopeType, e.ScopeKey, e.SandboxRole });
            entity.HasIndex(e => new { e.OwnerSubject, e.State, e.UpdatedAtUtc });
        });

        modelBuilder.Entity<SandboxSessionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SessionId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.ScopeType).IsRequired().HasMaxLength(160);
            entity.Property(e => e.ScopeKey).IsRequired().HasMaxLength(160);
            entity.Property(e => e.SandboxRole).IsRequired().HasMaxLength(80);
            entity.Property(e => e.SessionKey).IsRequired().HasMaxLength(160);
            entity.Property(e => e.ChannelId).HasMaxLength(120);
            entity.Property(e => e.SenderId).HasMaxLength(120);
            entity.Property(e => e.OwnerSubject).IsRequired().HasMaxLength(256);
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();

            entity.HasIndex(e => e.SessionId).IsUnique();
            entity.HasIndex(e => new { e.OwnerSubject, e.ScopeType, e.ScopeKey, e.SandboxRole, e.SessionKey }).IsUnique();

            entity.HasOne(e => e.SandboxInstance)
                .WithMany(s => s.Sessions)
                .HasForeignKey(e => e.SandboxInstanceEntityId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SandboxAssetEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MediaId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(512);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(512);
            entity.Property(e => e.MimeType).IsRequired().HasMaxLength(120);
            entity.Property(e => e.ContentHash).HasMaxLength(128);
            entity.Property(e => e.StoragePath).HasMaxLength(1024);
            entity.Property(e => e.AssetRole).IsRequired().HasMaxLength(80);
            entity.Property(e => e.CreatedAtUtc).IsRequired();

            entity.HasIndex(e => e.MediaId).IsUnique();
            entity.HasIndex(e => new { e.SandboxInstanceEntityId, e.CreatedAtUtc });

            entity.HasOne(e => e.SandboxInstance)
                .WithMany(s => s.Assets)
                .HasForeignKey(e => e.SandboxInstanceEntityId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SandboxSession)
                .WithMany(s => s.Assets)
                .HasForeignKey(e => e.SandboxSessionEntityId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
