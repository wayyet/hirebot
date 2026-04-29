using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddHiringPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HiringArtifacts",
                columns: table => new
                {
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LogicalPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperatorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringSessions", x => x.SessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_SessionId_IsFinal",
                table: "HiringArtifacts",
                columns: new[] { "SessionId", "IsFinal" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_SessionId_Kind_LogicalPath",
                table: "HiringArtifacts",
                columns: new[] { "SessionId", "Kind", "LogicalPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringArtifacts_UploadedAtUtc",
                table: "HiringArtifacts",
                column: "UploadedAtUtc");

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
                name: "IX_HiringArtifactUploads_SessionId_Kind_LogicalPath_CompletedA~",
                table: "HiringArtifactUploads",
                columns: new[] { "SessionId", "Kind", "LogicalPath", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringAuditLogs_HireId_TimestampUtc",
                table: "HiringAuditLogs",
                columns: new[] { "HireId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringAuditLogs_SessionId_TimestampUtc",
                table: "HiringAuditLogs",
                columns: new[] { "SessionId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringSessions_CreatedAtUtc",
                table: "HiringSessions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HiringSessions_HireId",
                table: "HiringSessions",
                column: "HireId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HiringArtifacts");

            migrationBuilder.DropTable(
                name: "HiringArtifactUploadParts");

            migrationBuilder.DropTable(
                name: "HiringArtifactUploads");

            migrationBuilder.DropTable(
                name: "HiringAuditLogs");

            migrationBuilder.DropTable(
                name: "HiringSessions");
        }
    }
}
