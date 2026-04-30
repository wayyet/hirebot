using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class HiringWorkflowState_v1 : Migration
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
                name: "HiringCredentialBindings",
                columns: table => new
                {
                    BindingId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CredentialSlot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SecretRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AuthKind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TargetSystem = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    TodoId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    BindingStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringCredentialBindings", x => x.BindingId);
                });

            migrationBuilder.CreateTable(
                name: "HiringRuntimeStates",
                columns: table => new
                {
                    HireId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentStage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CollectionPhase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiringRuntimeStates", x => x.HireId);
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
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SandboxInstances", x => x.Id);
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

            migrationBuilder.CreateIndex(
                name: "IX_HiringCredentialBindings_HireId_UpdatedAtUtc",
                table: "HiringCredentialBindings",
                columns: new[] { "HireId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HiringCredentialBindings_SessionId_CredentialSlot",
                table: "HiringCredentialBindings",
                columns: new[] { "SessionId", "CredentialSlot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HiringRuntimeStates_SessionId",
                table: "HiringRuntimeStates",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_HiringRuntimeStates_UpdatedAtUtc",
                table: "HiringRuntimeStates",
                column: "UpdatedAtUtc");

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
                name: "IX_SandboxInstances_OwnerSubject_ScopeType_ScopeKey_SandboxRole",
                table: "SandboxInstances",
                columns: new[] { "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_OwnerSubject_State_UpdatedAtUtc",
                table: "SandboxInstances",
                columns: new[] { "OwnerSubject", "State", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SandboxInstances_SandboxId",
                table: "SandboxInstances",
                column: "SandboxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SandboxSessions_OwnerSubject_ScopeType_ScopeKey_SandboxRole~",
                table: "SandboxSessions",
                columns: new[] { "OwnerSubject", "ScopeType", "ScopeKey", "SandboxRole", "SessionKey" },
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationReports");

            migrationBuilder.DropTable(
                name: "HiringCredentialBindings");

            migrationBuilder.DropTable(
                name: "HiringRuntimeStates");

            migrationBuilder.DropTable(
                name: "SandboxAssets");

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
