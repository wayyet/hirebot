using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.EfCoreMigration.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeChatImConfig_Reconcile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvaluationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
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
                name: "EvaluationAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "EvaluationReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationAssets_RelativePath",
                table: "EvaluationAssets",
                column: "RelativePath");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationAssets_SessionEntityId_AssetType",
                table: "EvaluationAssets",
                columns: new[] { "SessionEntityId", "AssetType" });

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
                name: "IX_EvaluationSessions_EvaluatorHireId_TargetHireId",
                table: "EvaluationSessions",
                columns: new[] { "EvaluatorHireId", "TargetHireId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_OwnerSubject_EmployeeId_UpdatedAtUtc",
                table: "EvaluationSessions",
                columns: new[] { "OwnerSubject", "EmployeeId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_SessionId",
                table: "EvaluationSessions",
                column: "SessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationReports");

            migrationBuilder.DropTable(
                name: "EvaluationAssets");

            migrationBuilder.DropTable(
                name: "EvaluationSessions");
        }
    }
}
