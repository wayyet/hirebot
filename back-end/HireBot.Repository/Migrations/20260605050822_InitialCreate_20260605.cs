using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate_20260605 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FamilyName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GivenName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    ConversationId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.ConversationId);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TargetHireId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TargetSandboxId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EvaluatorHireId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EvaluatorSandboxId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationWorkspaceStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationWorkspaceStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HiringArtifacts",
                columns: table => new
                {
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LogicalPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    IsFinal = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringArtifacts", x => x.ArtifactId);
                });

            migrationBuilder.CreateTable(
                name: "HiringArtifactUploadParts",
                columns: table => new
                {
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UploadId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartNumber = table.Column<int>(type: "integer", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringArtifactUploadParts", x => x.PartId);
                });

            migrationBuilder.CreateTable(
                name: "HiringArtifactUploads",
                columns: table => new
                {
                    UploadId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LogicalPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TotalSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PartSizeBytes = table.Column<int>(type: "integer", nullable: false),
                    TotalParts = table.Column<int>(type: "integer", nullable: false),
                    ExpectedSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TempStorageDirectory = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AbortedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringArtifactUploads", x => x.UploadId);
                });

            migrationBuilder.CreateTable(
                name: "HiringAuditLogs",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ArtifactId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BeforeSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AfterSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DetailJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringAuditLogs", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "HiringExternalConfigs",
                columns: table => new
                {
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ConfigJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringExternalConfigs", x => x.HireId);
                });

            migrationBuilder.CreateTable(
                name: "HiringMaterialFiles",
                columns: table => new
                {
                    MaterialFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedCategoryTitle = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    WorkspaceRelativePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringMaterialFiles", x => x.MaterialFileId);
                });

            migrationBuilder.CreateTable(
                name: "HiringSessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PackageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PackageVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PackageHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceZipSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceZipStoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SourceZipSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OperatorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringSessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "HiringStageProgresses",
                columns: table => new
                {
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CurrentStage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PackagingTestCasesStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    StageOverridesJson = table.Column<string>(type: "jsonb", nullable: true),
                    DownstreamRunsJson = table.Column<string>(type: "jsonb", nullable: true),
                    UploadedFilesJson = table.Column<string>(type: "jsonb", nullable: true),
                    PackageStructureJson = table.Column<string>(type: "jsonb", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringStageProgresses", x => x.HireId);
                });

            migrationBuilder.CreateTable(
                name: "HiringStructuredData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FieldKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FieldValue = table.Column<string>(type: "text", nullable: true),
                    CollectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringStructuredData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Instances",
                columns: table => new
                {
                    InstanceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InstanceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BasedOnTemplateId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FromInstanceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ActiveBranchId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    EvalReportId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    FinalPackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OwnerUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CurrentVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RuntimeSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DescribeDocument = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instances", x => x.InstanceId);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExternalMessageId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ExternalUserId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    DeliveryStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "SandboxInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SandboxId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SandboxRole = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProvisioningMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    GatewayEndpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    UseCase = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TemplateId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsInitialized = table.Column<bool>(type: "boolean", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SessionEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RelatedKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    RelativePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PublicUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationAssets_EvaluationSessions_SessionEntityId",
                        column: x => x.SessionEntityId,
                        principalTable: "EvaluationSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SandboxSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SandboxInstanceEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ScopeKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SandboxRole = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SessionKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SenderId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SandboxSessions_SandboxInstances_SandboxInstanceEntityId",
                        column: x => x.SandboxInstanceEntityId,
                        principalTable: "SandboxInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SessionEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    DimensionScoresJson = table.Column<string>(type: "text", nullable: false),
                    SummaryJson = table.Column<string>(type: "text", nullable: false),
                    ReportJsonAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportHtmlAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationReports_EvaluationAssets_ReportHtmlAssetId",
                        column: x => x.ReportHtmlAssetId,
                        principalTable: "EvaluationAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EvaluationReports_EvaluationAssets_ReportJsonAssetId",
                        column: x => x.ReportJsonAssetId,
                        principalTable: "EvaluationAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EvaluationReports_EvaluationSessions_SessionEntityId",
                        column: x => x.SessionEntityId,
                        principalTable: "EvaluationSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SandboxAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SandboxInstanceEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    SandboxSessionEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    MediaId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    AssetRole = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SandboxAssets_SandboxInstances_SandboxInstanceEntityId",
                        column: x => x.SandboxInstanceEntityId,
                        principalTable: "SandboxInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SandboxAssets_SandboxSessions_SandboxSessionEntityId",
                        column: x => x.SandboxSessionEntityId,
                        principalTable: "SandboxSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_ExternalUserId",
                table: "AppUsers",
                column: "ExternalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId",
                table: "AppUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId_ExternalUserId",
                table: "AppUsers",
                columns: new[] { "TenantId", "ExternalUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId_Username",
                table: "AppUsers",
                columns: new[] { "TenantId", "Username" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_InstanceId_OwnerUserId_Channel",
                table: "Conversations",
                columns: new[] { "InstanceId", "OwnerUserId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TenantId_UpdatedAt",
                table: "Conversations",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationAssets_RelativePath",
                table: "EvaluationAssets",
                column: "RelativePath");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationAssets_SessionEntityId_AssetType",
                table: "EvaluationAssets",
                columns: new[] { "SessionEntityId", "AssetType" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationAssets_TenantId",
                table: "EvaluationAssets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationReports_ReportHtmlAssetId",
                table: "EvaluationReports",
                column: "ReportHtmlAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationReports_ReportJsonAssetId",
                table: "EvaluationReports",
                column: "ReportJsonAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationReports_SessionEntityId_Iteration",
                table: "EvaluationReports",
                columns: new[] { "SessionEntityId", "Iteration" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationReports_TenantId",
                table: "EvaluationReports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_SessionId",
                table: "EvaluationSessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_TenantId_EvaluatorHireId_TargetHireId",
                table: "EvaluationSessions",
                columns: new[] { "TenantId", "EvaluatorHireId", "TargetHireId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_TenantId_OwnerSubject_EmployeeId_Updated~",
                table: "EvaluationSessions",
                columns: new[] { "TenantId", "OwnerSubject", "EmployeeId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationWorkspaceStates_TenantId_OwnerSubject_EmployeeId",
                table: "EvaluationWorkspaceStates",
                columns: new[] { "TenantId", "OwnerSubject", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationWorkspaceStates_UpdatedAtUtc",
                table: "EvaluationWorkspaceStates",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_TenantId_SessionId_IsFinal",
                table: "HiringArtifacts",
                columns: new[] { "TenantId", "SessionId", "IsFinal" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_TenantId_SessionId_Kind_LogicalPath",
                table: "HiringArtifacts",
                columns: new[] { "TenantId", "SessionId", "Kind", "LogicalPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_UploadedAtUtc",
                table: "HiringArtifacts",
                column: "UploadedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifactUploadParts_TenantId",
                table: "HiringArtifactUploadParts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifactUploadParts_UploadId_PartNumber",
                table: "HiringArtifactUploadParts",
                columns: new[] { "UploadId", "PartNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifactUploads_CreatedAtUtc",
                table: "HiringArtifactUploads",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifactUploads_TenantId_SessionId_Kind_LogicalPath_C~",
                table: "HiringArtifactUploads",
                columns: new[] { "TenantId", "SessionId", "Kind", "LogicalPath", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringAuditLogs_TenantId_HireId_TimestampUtc",
                table: "HiringAuditLogs",
                columns: new[] { "TenantId", "HireId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringAuditLogs_TenantId_SessionId_TimestampUtc",
                table: "HiringAuditLogs",
                columns: new[] { "TenantId", "SessionId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringExternalConfigs_TenantId_UpdatedAtUtc",
                table: "HiringExternalConfigs",
                columns: new[] { "TenantId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringMaterialFiles_HireId_SessionId_UploadedAtUtc",
                table: "HiringMaterialFiles",
                columns: new[] { "HireId", "SessionId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringMaterialFiles_SessionId_RelativePath",
                table: "HiringMaterialFiles",
                columns: new[] { "SessionId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringMaterialFiles_Sha256",
                table: "HiringMaterialFiles",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_HiringSessions_CreatedAtUtc",
                table: "HiringSessions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HiringSessions_HireId",
                table: "HiringSessions",
                column: "HireId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringStageProgresses_TenantId_UpdatedAtUtc",
                table: "HiringStageProgresses",
                columns: new[] { "TenantId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringStructuredData_HireId",
                table: "HiringStructuredData",
                column: "HireId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringStructuredData_HireId_FieldKey",
                table: "HiringStructuredData",
                columns: new[] { "HireId", "FieldKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_BasedOnTemplateId",
                table: "Instances",
                column: "BasedOnTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_FromInstanceId",
                table: "Instances",
                column: "FromInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_OwnerUserId_InstanceType_Status",
                table: "Instances",
                columns: new[] { "OwnerUserId", "InstanceType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_TenantId_DepartmentId_InstanceType_Status",
                table: "Instances",
                columns: new[] { "TenantId", "DepartmentId", "InstanceType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_Channel_ExternalMessageId",
                table: "Messages",
                columns: new[] { "Channel", "ExternalMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_CreatedAt",
                table: "Messages",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_InstanceId_Channel_CreatedAt",
                table: "Messages",
                columns: new[] { "InstanceId", "Channel", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_InstanceId_CreatedAt",
                table: "Messages",
                columns: new[] { "InstanceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxAssets_MediaId",
                table: "SandboxAssets",
                column: "MediaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxAssets_SandboxInstanceEntityId_CreatedAtUtc",
                table: "SandboxAssets",
                columns: new[] { "SandboxInstanceEntityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxAssets_SandboxSessionEntityId",
                table: "SandboxAssets",
                column: "SandboxSessionEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxAssets_TenantId",
                table: "SandboxAssets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_OwnerSubject_ScopeType_ScopeKey_SandboxRole",
                table: "SandboxInstances",
                columns: new[] { "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_OwnerSubject_State_UpdatedAtUtc",
                table: "SandboxInstances",
                columns: new[] { "OwnerSubject", "State", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_OwnerSubject_TemplateId_SandboxRole_State",
                table: "SandboxInstances",
                columns: new[] { "OwnerSubject", "TemplateId", "SandboxRole", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_SandboxId",
                table: "SandboxInstances",
                column: "SandboxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_SandboxInstanceEntityId",
                table: "SandboxSessions",
                column: "SandboxInstanceEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_SessionId",
                table: "SandboxSessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_TenantId_OwnerSubject_ScopeType_ScopeKey_Sa~",
                table: "SandboxSessions",
                columns: new[] { "TenantId", "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole", "SessionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "EvaluationReports");

            migrationBuilder.DropTable(
                name: "EvaluationWorkspaceStates");

            migrationBuilder.DropTable(
                name: "HiringArtifacts");

            migrationBuilder.DropTable(
                name: "HiringArtifactUploadParts");

            migrationBuilder.DropTable(
                name: "HiringArtifactUploads");

            migrationBuilder.DropTable(
                name: "HiringAuditLogs");

            migrationBuilder.DropTable(
                name: "HiringExternalConfigs");

            migrationBuilder.DropTable(
                name: "HiringMaterialFiles");

            migrationBuilder.DropTable(
                name: "HiringSessions");

            migrationBuilder.DropTable(
                name: "HiringStageProgresses");

            migrationBuilder.DropTable(
                name: "HiringStructuredData");

            migrationBuilder.DropTable(
                name: "Instances");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "SandboxAssets");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "EvaluationAssets");

            migrationBuilder.DropTable(
                name: "SandboxSessions");

            migrationBuilder.DropTable(
                name: "EvaluationSessions");

            migrationBuilder.DropTable(
                name: "SandboxInstances");
        }
    }
}
