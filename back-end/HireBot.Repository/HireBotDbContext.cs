using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HireBot.Abstraction.Infrastructure.Multitenancy;
using HireBot.Abstraction.Models.User;
using HireBot.Repository.Entities;
using HireBot.Repository.Extensions;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HireBot.Repository;

public sealed class HireBotDbContext : DbContext
{
    private readonly ITenantContextProvider? _tenantContextProvider;
    private string? _tenantId;

    // 无参构造函数（仅用于 EF Core 迁移工具）
    public HireBotDbContext(DbContextOptions<HireBotDbContext> options) : base(options)
    {
    }

    // 依赖注入构造函数（运行时使用）
    public HireBotDbContext(
        DbContextOptions<HireBotDbContext> options,
        ITenantContextProvider tenantContextProvider) : base(options)
    {
        _tenantContextProvider = tenantContextProvider;
    }

    /// <summary>
    /// 当前租户ID（懒加载）
    /// 用于全局查询过滤器
    /// </summary>
    public string? TenantId
    {
        get
        {
            if (_tenantId == null)
            {
                _tenantId = _tenantContextProvider?.GetTenantId() ?? "default";
            }
            return _tenantId;
        }
        set => _tenantId = value;
    }

    private static readonly ValueComparer<Dictionary<string, string>?> SandboxMetadataComparer = new(
        (left, right) => DictionariesEqual(left, right),
        value => GetDictionaryHashCode(value),
        value => value == null ? null : new Dictionary<string, string>(value, StringComparer.Ordinal));

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
    public DbSet<AppUserEntity> AppUsers { get; set; }
    public DbSet<EvaluationSessionEntity> EvaluationSessions { get; set; }
    public DbSet<EvaluationAssetEntity> EvaluationAssets { get; set; }
    public DbSet<EvaluationReportEntity> EvaluationReports { get; set; }
    public DbSet<EvaluationWorkspaceStateEntity> EvaluationWorkspaceStates { get; set; }

    public DbSet<HiringSessionEntity> HiringSessions { get; set; }
    public DbSet<HiringStageProgressEntity> HiringStageProgresses { get; set; }
    public DbSet<HiringStructuredDataEntity> HiringStructuredData { get; set; }
    public DbSet<HiringExternalConfigEntity> HiringExternalConfigs { get; set; }
    public DbSet<HiringSkillLinkConfigEntity> HiringSkillLinkConfigs { get; set; }
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

        // AppUser (多租户用户表：同一 Keycloak 用户在不同租户下有独立记录)
        modelBuilder.Entity<AppUserEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36).ValueGeneratedNever();
            entity.Property(e => e.ExternalUserId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(128);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.FamilyName).HasMaxLength(256);
            entity.Property(e => e.GivenName).HasMaxLength(256);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.LastSeenAt).IsRequired();

            // 唯一约束：同一租户下，外部用户ID唯一
            entity.HasIndex(e => new { e.TenantId, e.ExternalUserId }).IsUnique();
            
            // 其他索引
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.Username });
            entity.HasIndex(e => e.ExternalUserId);
        });

        modelBuilder.Entity<EvaluationSessionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SessionId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.OwnerSubject).IsRequired().HasMaxLength(120);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
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
            entity.HasIndex(e => new { e.TenantId, e.OwnerSubject, e.EmployeeId, e.UpdatedAtUtc });
            entity.HasIndex(e => new { e.TenantId, e.EvaluatorHireId, e.TargetHireId });
        });

        modelBuilder.Entity<EvaluationAssetEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.Property(e => e.AssetType).IsRequired().HasMaxLength(40);
            entity.Property(e => e.RelatedKey).HasMaxLength(160);
            entity.Property(e => e.RelativePath).IsRequired().HasMaxLength(512);
            entity.Property(e => e.PublicUrl).IsRequired().HasMaxLength(512);
            entity.Property(e => e.MimeType).IsRequired().HasMaxLength(120);
            entity.Property(e => e.ContentHash).IsRequired().HasMaxLength(128);
            entity.Property(e => e.SourceType).IsRequired().HasMaxLength(40);
            entity.Property(e => e.CreatedAtUtc).IsRequired();

            entity.HasIndex(e => e.TenantId);
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
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.Property(e => e.OverallScore).HasColumnType("numeric(6,2)");
            entity.Property(e => e.DimensionScoresJson).IsRequired();
            entity.Property(e => e.SummaryJson).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();

            entity.HasIndex(e => e.TenantId);
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
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OwnerSubject).IsRequired().HasMaxLength(120);
                entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
                entity.Property(e => e.EmployeeId).IsRequired().HasMaxLength(120);
                entity.Property(e => e.PayloadJson).IsRequired();
                entity.Property(e => e.CreatedAtUtc).IsRequired();
                entity.Property(e => e.UpdatedAtUtc).IsRequired();

                entity.HasIndex(e => new { e.TenantId, e.OwnerSubject, e.EmployeeId }).IsUnique();
                entity.HasIndex(e => e.UpdatedAtUtc);
            });

        modelBuilder.Entity<HiringSessionEntity>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.HasIndex(e => e.HireId).IsUnique();
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<HiringStageProgressEntity>(entity =>
        {
            entity.HasKey(e => e.HireId);
            entity.Property(e => e.HireId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.Property(e => e.CurrentStage).IsRequired().HasMaxLength(40);
            entity.Property(e => e.PackagingTestCasesStatus).HasMaxLength(40);
            entity.Property(e => e.StageOverridesJson).HasColumnType("jsonb");
            entity.Property(e => e.DownstreamRunsJson).HasColumnType("jsonb");
            entity.Property(e => e.UploadedFilesJson).HasColumnType("jsonb");
            entity.Property(e => e.PackageStructureJson).HasColumnType("jsonb");
            entity.Property(e => e.UpdatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedBy).HasMaxLength(256);

            entity.HasIndex(e => new { e.TenantId, e.UpdatedAtUtc });
        });

        modelBuilder.Entity<HiringStructuredDataEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.HireId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.Property(e => e.FieldKey).IsRequired().HasMaxLength(256);
            entity.Property(e => e.FieldValue);
            entity.Property(e => e.CollectedAtUtc).IsRequired();

            entity.HasIndex(e => e.HireId);
            entity.HasIndex(e => new { e.HireId, e.FieldKey });
        });

        modelBuilder.Entity<HiringExternalConfigEntity>(entity =>
        {
            entity.HasKey(e => e.HireId);
            entity.Property(e => e.HireId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.Property(e => e.ConfigJson).IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.UpdatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedBy).HasMaxLength(256);

            entity.HasIndex(e => new { e.TenantId, e.UpdatedAtUtc });
        });

        modelBuilder.Entity<HiringSkillLinkConfigEntity>(entity =>
        {
            entity.HasKey(e => e.HireId);
            entity.Property(e => e.HireId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.Property(e => e.ConfigJson).IsRequired().HasDefaultValue("{}");
            entity.Property(e => e.UpdatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedBy).HasMaxLength(256);

            entity.HasIndex(e => new { e.TenantId, e.UpdatedAtUtc });
        });

        modelBuilder.Entity<HiringArtifactEntity>(entity =>
        {
            entity.HasKey(e => e.ArtifactId);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => new { e.TenantId, e.SessionId, e.Kind, e.LogicalPath }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.SessionId, e.IsFinal });
            entity.HasIndex(e => e.UploadedAtUtc);
        });

        modelBuilder.Entity<HiringMaterialFileEntity>(entity =>
        {
            entity.ToTable("HiringMaterialFiles");
            entity.HasKey(e => e.MaterialFileId);
            entity.Property(e => e.HireId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.SessionId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.RelativePath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(512);
            entity.Property(e => e.StoragePath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.Format).IsRequired().HasMaxLength(32);
            entity.Property(e => e.MimeType).HasMaxLength(120);
            entity.Property(e => e.SizeBytes).IsRequired();
            entity.Property(e => e.Sha256).IsRequired().HasMaxLength(64);
            entity.Property(e => e.RequestedCategoryTitle).HasMaxLength(160);
            entity.Property(e => e.WorkspaceRelativePath).HasMaxLength(1024);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.OperatorId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.UploadedBy).IsRequired().HasMaxLength(256);
            entity.Property(e => e.UploadedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();
            entity.Property(e => e.DeletedAtUtc);

            entity.HasIndex(e => new { e.SessionId, e.RelativePath }).IsUnique();
            entity.HasIndex(e => new { e.HireId, e.SessionId, e.UploadedAtUtc });
            entity.HasIndex(e => e.Sha256);
        });

        modelBuilder.Entity<HiringArtifactUploadEntity>(entity =>
        {
            entity.HasKey(e => e.UploadId);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => new { e.TenantId, e.SessionId, e.Kind, e.LogicalPath, e.CompletedAtUtc });
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<HiringArtifactUploadPartEntity>(entity =>
        {
            entity.HasKey(e => e.PartId);
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.UploadId, e.PartNumber }).IsUnique();
        });

        modelBuilder.Entity<HiringAuditLogEntity>(entity =>
        {
            entity.HasKey(e => e.AuditId);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
            entity.HasIndex(e => new { e.TenantId, e.SessionId, e.TimestampUtc });
            entity.HasIndex(e => new { e.TenantId, e.HireId, e.TimestampUtc });
        });

        modelBuilder.Entity<InstanceEntity>(entity =>
        {
            entity.ToTable("Instances");
            entity.HasKey(e => e.InstanceId);
            entity.Property(e => e.InstanceId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.InstanceType).IsRequired().HasMaxLength(40);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(40);
            entity.Property(e => e.BasedOnTemplateId).HasMaxLength(128);
            entity.Property(e => e.FromInstanceId).HasMaxLength(120);
            entity.Property(e => e.ActiveBranchId).HasMaxLength(120);
            entity.Property(e => e.EvalReportId).HasMaxLength(120);
            entity.Property(e => e.OwnerUserId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DepartmentId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.CurrentVersion).IsRequired().HasMaxLength(80);
            entity.Property(e => e.RuntimeSnapshotJson);
            entity.Property(e => e.Description);
            entity.Property(e => e.DescribeDocument);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasIndex(e => new { e.TenantId, e.DepartmentId, e.InstanceType, e.Status });
            entity.HasIndex(e => new { e.OwnerUserId, e.InstanceType, e.Status });
            entity.HasIndex(e => e.FromInstanceId);
            entity.HasIndex(e => e.BasedOnTemplateId);
        });

        modelBuilder.Entity<ConversationEntity>(entity =>
        {
            entity.ToTable("Conversations");
            entity.HasKey(e => e.ConversationId);
            entity.Property(e => e.ConversationId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.InstanceId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.OwnerUserId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Channel).IsRequired().HasMaxLength(40);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();

            entity.HasIndex(e => new { e.InstanceId, e.OwnerUserId, e.Channel });
            entity.HasIndex(e => new { e.TenantId, e.UpdatedAt });
        });

        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.ToTable("Messages");
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.MessageId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.ConversationId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.InstanceId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(40);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Channel).IsRequired().HasMaxLength(40);
            entity.Property(e => e.ExternalMessageId).HasMaxLength(160);
            entity.Property(e => e.ExternalUserId).HasMaxLength(160);
            entity.Property(e => e.DeliveryStatus).HasMaxLength(40);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1024);
            entity.Property(e => e.MetadataJson);
            entity.Property(e => e.CreatedAt).IsRequired();

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
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null))
                .Metadata.SetValueComparer(SandboxMetadataComparer);
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
            entity.Property(e => e.TenantId).IsRequired().HasMaxLength(128);
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
            entity.HasIndex(e => new { e.TenantId, e.OwnerSubject, e.ScopeType, e.ScopeKey, e.SandboxRole, e.SessionKey }).IsUnique();

            entity.HasOne(e => e.SandboxInstance)
                .WithMany(s => s.Sessions)
                .HasForeignKey(e => e.SandboxInstanceEntityId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SandboxAssetEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.Property(e => e.MediaId).IsRequired().HasMaxLength(120);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(512);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(512);
            entity.Property(e => e.MimeType).IsRequired().HasMaxLength(120);
            entity.Property(e => e.ContentHash).HasMaxLength(128);
            entity.Property(e => e.StoragePath).HasMaxLength(1024);
            entity.Property(e => e.AssetRole).IsRequired().HasMaxLength(80);
            entity.Property(e => e.CreatedAtUtc).IsRequired();

            entity.HasIndex(e => e.TenantId);
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

        // ============================================================
        // 应用多租户全局查询过滤器
        // 所有实现 ITenant 接口的实体将自动应用租户隔离
        // ============================================================
        modelBuilder.ApplyTenantQueryFilters(() => TenantId);
    }

    private static bool DictionariesEqual(
        Dictionary<string, string>? left,
        Dictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var otherValue) || !string.Equals(value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetDictionaryHashCode(Dictionary<string, string>? value)
    {
        if (value is null || value.Count == 0)
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var key in value.Keys.OrderBy(static item => item, StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value[key], StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
