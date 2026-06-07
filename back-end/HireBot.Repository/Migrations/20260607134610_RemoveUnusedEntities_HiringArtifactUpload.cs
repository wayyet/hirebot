using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedEntities_HiringArtifactUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HiringArtifactUploadParts");

            migrationBuilder.DropTable(
                name: "HiringArtifactUploads");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HiringArtifactUploadParts",
                columns: table => new
                {
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartNumber = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UploadId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    AbortedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpectedSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LogicalPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PartSizeBytes = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TempStorageDirectory = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TotalParts = table.Column<int>(type: "integer", nullable: false),
                    TotalSizeBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringArtifactUploads", x => x.UploadId);
                });

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
        }
    }
}
