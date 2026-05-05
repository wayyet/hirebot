using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddInstancesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    conversation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner_user_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.conversation_id);
                });

            migrationBuilder.CreateTable(
                name: "ImConfigs",
                columns: table => new
                {
                    config_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner_user_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    platform = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    connection_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    webhook_path = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    app_id = table.Column<string>(type: "text", nullable: true),
                    app_secret = table.Column<string>(type: "text", nullable: true),
                    encrypt_key = table.Column<string>(type: "text", nullable: true),
                    token = table.Column<string>(type: "text", nullable: true),
                    aes_key = table.Column<string>(type: "text", nullable: true),
                    verification_token = table.Column<string>(type: "text", nullable: true),
                    corp_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    agent_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    agent_secret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    configured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImConfigs", x => x.config_id);
                });

            migrationBuilder.CreateTable(
                name: "Instances",
                columns: table => new
                {
                    instance_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    instance_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    via_quick_clone = table.Column<bool>(type: "boolean", nullable: false),
                    based_on_template_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    from_instance_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    eval_report_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    owner_user_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    department_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    current_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instances", x => x.instance_id);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    message_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    external_message_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    external_user_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    delivery_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    metadata_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.message_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_instance_id_owner_user_id_channel",
                table: "Conversations",
                columns: new[] { "instance_id", "owner_user_id", "channel" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_tenant_id_updated_at",
                table: "Conversations",
                columns: new[] { "tenant_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ImConfigs_instance_id_platform",
                table: "ImConfigs",
                columns: new[] { "instance_id", "platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImConfigs_platform_status",
                table: "ImConfigs",
                columns: new[] { "platform", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_ImConfigs_tenant_id_owner_user_id",
                table: "ImConfigs",
                columns: new[] { "tenant_id", "owner_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_based_on_template_id",
                table: "Instances",
                column: "based_on_template_id");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_from_instance_id",
                table: "Instances",
                column: "from_instance_id");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_owner_user_id_instance_type_status",
                table: "Instances",
                columns: new[] { "owner_user_id", "instance_type", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_tenant_id_department_id_instance_type_status",
                table: "Instances",
                columns: new[] { "tenant_id", "department_id", "instance_type", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_channel_external_message_id",
                table: "Messages",
                columns: new[] { "channel", "external_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_conversation_id_created_at",
                table: "Messages",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_instance_id_channel_created_at",
                table: "Messages",
                columns: new[] { "instance_id", "channel", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_instance_id_created_at",
                table: "Messages",
                columns: new[] { "instance_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "ImConfigs");

            migrationBuilder.DropTable(
                name: "Instances");

            migrationBuilder.DropTable(
                name: "Messages");
        }
    }
}
