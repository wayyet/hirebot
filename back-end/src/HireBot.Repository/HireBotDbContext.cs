using HireBot.Abstraction.Models.User;
using HireBot.Repository.Entities;
using HireBot.Repository.Extensions;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Repository;

public sealed class HireBotDbContext(DbContextOptions<HireBotDbContext> options) : DbContext(options)
{
    public override int SaveChanges()
    {
        TruncateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TruncateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void TruncateTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                continue;

            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is DateTimeOffset dto
                    && (property.Metadata.ClrType == typeof(DateTimeOffset)
                        || property.Metadata.ClrType == typeof(DateTimeOffset?)))
                {
                    property.CurrentValue = dto.TruncateToMinute();
                }
            }
        }
    }

    public DbSet<User> Users { get; set; }
    public DbSet<EvaluationSessionEntity> EvaluationSessions { get; set; }
    public DbSet<EvaluationAssetEntity> EvaluationAssets { get; set; }
    public DbSet<EvaluationReportEntity> EvaluationReports { get; set; }
    public DbSet<EvaluationWorkspaceStateEntity> EvaluationWorkspaceStates { get; set; }

    public DbSet<HiringSessionEntity> HiringSessions { get; set; }
    public DbSet<HiringRuntimeStateEntity> HiringRuntimeStates { get; set; }
    public DbSet<HiringCredentialBindingEntity> HiringCredentialBindings { get; set; }
    public DbSet<HiringArtifactEntity> HiringArtifacts { get; set; }
    public DbSet<HiringMaterialFileEntity> HiringMaterialFiles { get; set; }
    public DbSet<HiringArtifactUploadEntity> HiringArtifactUploads { get; set; }
    public DbSet<HiringArtifactUploadPartEntity> HiringArtifactUploadParts { get; set; }
    public DbSet<HiringAuditLogEntity> HiringAuditLogs { get; set; }
    public DbSet<InstanceEntity> Instances { get; set; }
    public DbSet<ConversationEntity> Conversations { get; set; }
    public DbSet<MessageEntity> Messages { get; set; }
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

            modelBuilder.Entity<EvaluationWorkspaceStateEntity>(entity =>
            {
                entity.HasKey(e => new { e.OwnerSubject, e.EmployeeId });
                entity.Property(e => e.OwnerSubject).IsRequired().HasMaxLength(120);
                entity.Property(e => e.EmployeeId).IsRequired().HasMaxLength(120);
                entity.Property(e => e.PayloadJson).IsRequired();
                entity.Property(e => e.CreatedAtUtc).IsRequired();
                entity.Property(e => e.UpdatedAtUtc).IsRequired();

                entity.HasIndex(e => e.UpdatedAtUtc);
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
            entity.Property(e => e.PackagesJson).IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.WorkflowStateJson).IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.ConversationCacheJson).IsRequired().HasDefaultValue("{}");
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
            entity.Property(e => e.HandoffId).HasMaxLength(160);
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

        modelBuilder.Entity<HiringMaterialFileEntity>(entity =>
        {
            entity.ToTable("hiring_material_files");
            entity.HasKey(e => e.MaterialFileId);
            entity.Property(e => e.MaterialFileId).HasColumnName("material_file_id");
            entity.Property(e => e.HireId).HasColumnName("hire_id").IsRequired().HasMaxLength(64);
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired().HasMaxLength(64);
            entity.Property(e => e.RelativePath).HasColumnName("relative_path").IsRequired().HasMaxLength(1024);
            entity.Property(e => e.OriginalFileName).HasColumnName("original_file_name").IsRequired().HasMaxLength(512);
            entity.Property(e => e.StoragePath).HasColumnName("storage_path").IsRequired().HasMaxLength(1024);
            entity.Property(e => e.Format).HasColumnName("format").IsRequired().HasMaxLength(32);
            entity.Property(e => e.MimeType).HasColumnName("mime_type").HasMaxLength(120);
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes").IsRequired();
            entity.Property(e => e.Sha256).HasColumnName("sha256").IsRequired().HasMaxLength(64);
            entity.Property(e => e.RequestedCategoryTitle).HasColumnName("requested_category_title").HasMaxLength(160);
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired().HasMaxLength(128);
            entity.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired().HasMaxLength(128);
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by").IsRequired().HasMaxLength(256);
            entity.Property(e => e.UploadedAtUtc).HasColumnName("uploaded_at_utc").IsRequired();
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.Property(e => e.DeletedAtUtc).HasColumnName("deleted_at_utc");

            entity.HasIndex(e => new { e.SessionId, e.RelativePath }).IsUnique();
            entity.HasIndex(e => new { e.HireId, e.SessionId, e.UploadedAtUtc });
            entity.HasIndex(e => e.Sha256);
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

        modelBuilder.Entity<InstanceEntity>(entity =>
        {
            entity.ToTable("Instances");
            entity.HasKey(e => e.InstanceId);
            entity.Property(e => e.InstanceId).HasColumnName("instance_id").IsRequired().HasMaxLength(120);
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired().HasMaxLength(128);
            entity.Property(e => e.InstanceType).HasColumnName("instance_type").IsRequired().HasMaxLength(40);
            entity.Property(e => e.Status).HasColumnName("status").IsRequired().HasMaxLength(40);
            entity.Property(e => e.BasedOnTemplateId).HasColumnName("based_on_template_id").HasMaxLength(128);
            entity.Property(e => e.FromInstanceId).HasColumnName("from_instance_id").HasMaxLength(120);
            entity.Property(e => e.ActiveBranchId).HasColumnName("active_branch_id").HasMaxLength(120);
            entity.Property(e => e.EvalReportId).HasColumnName("eval_report_id").HasMaxLength(120);
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").IsRequired().HasMaxLength(256);
            entity.Property(e => e.DepartmentId).HasColumnName("department_id").IsRequired().HasMaxLength(128);
            entity.Property(e => e.CurrentVersion).HasColumnName("current_version").IsRequired().HasMaxLength(80);
            entity.Property(e => e.RuntimeSnapshotJson).HasColumnName("runtime_snapshot_json");
            entity.Property(e => e.DescribeDocument).HasColumnName("describe_document");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(e => new { e.TenantId, e.DepartmentId, e.InstanceType, e.Status });
            entity.HasIndex(e => new { e.OwnerUserId, e.InstanceType, e.Status });
            entity.HasIndex(e => e.FromInstanceId);
            entity.HasIndex(e => e.BasedOnTemplateId);
        });

        modelBuilder.Entity<ConversationEntity>(entity =>
        {
            entity.ToTable("Conversations");
            entity.HasKey(e => e.ConversationId);
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id").IsRequired().HasMaxLength(120);
            entity.Property(e => e.InstanceId).HasColumnName("instance_id").IsRequired().HasMaxLength(120);
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired().HasMaxLength(128);
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id").IsRequired().HasMaxLength(256);
            entity.Property(e => e.Channel).HasColumnName("channel").IsRequired().HasMaxLength(40);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(e => new { e.InstanceId, e.OwnerUserId, e.Channel });
            entity.HasIndex(e => new { e.TenantId, e.UpdatedAt });
        });

        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.ToTable("Messages");
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.MessageId).HasColumnName("message_id").IsRequired().HasMaxLength(120);
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id").IsRequired().HasMaxLength(120);
            entity.Property(e => e.InstanceId).HasColumnName("instance_id").IsRequired().HasMaxLength(120);
            entity.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired().HasMaxLength(128);
            entity.Property(e => e.Role).HasColumnName("role").IsRequired().HasMaxLength(40);
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.Channel).HasColumnName("channel").IsRequired().HasMaxLength(40);
            entity.Property(e => e.ExternalMessageId).HasColumnName("external_message_id").HasMaxLength(160);
            entity.Property(e => e.ExternalUserId).HasColumnName("external_user_id").HasMaxLength(160);
            entity.Property(e => e.DeliveryStatus).HasColumnName("delivery_status").HasMaxLength(40);
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(1024);
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt });
            entity.HasIndex(e => new { e.InstanceId, e.CreatedAt });
            entity.HasIndex(e => new { e.Channel, e.ExternalMessageId }).IsUnique();
            entity.HasIndex(e => new { e.InstanceId, e.Channel, e.CreatedAt });
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
            entity.HasIndex(e => new { e.OwnerSubject, e.TemplateId, e.SandboxRole, e.State });
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
