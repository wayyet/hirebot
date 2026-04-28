using HireBot.Abstraction.Models.User;
using HireBot.Repository.Entities;
using Microsoft.EntityFrameworkCore;

namespace HireBot.Repository;

public sealed class HireBotDbContext(DbContextOptions<HireBotDbContext> options) : DbContext(options)
{
    // 用户实体
    public DbSet<User> Users { get; set; }

    public DbSet<HiringSessionEntity> HiringSessions { get; set; }
    public DbSet<HiringArtifactEntity> HiringArtifacts { get; set; }
    public DbSet<HiringArtifactUploadEntity> HiringArtifactUploads { get; set; }
    public DbSet<HiringArtifactUploadPartEntity> HiringArtifactUploadParts { get; set; }
    public DbSet<HiringAuditLogEntity> HiringAuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置用户实体
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

        modelBuilder.Entity<HiringSessionEntity>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.HasIndex(e => e.HireId).IsUnique();
            entity.HasIndex(e => e.CreatedAtUtc);
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
    }
}
