using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RuntimeChatImConfig_v1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "MESSAGE",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "inapp");

            migrationBuilder.AddColumn<string>(
                name: "delivery_status",
                table: "MESSAGE",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_message",
                table: "MESSAGE",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_message_id",
                table: "MESSAGE",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_user_id",
                table: "MESSAGE",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metadata_json",
                table: "MESSAGE",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IM_CONFIG",
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
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    configured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IM_CONFIG", x => x.config_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGE_channel_external_message_id",
                table: "MESSAGE",
                columns: new[] { "channel", "external_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGE_instance_id_channel_created_at",
                table: "MESSAGE",
                columns: new[] { "instance_id", "channel", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_IM_CONFIG_instance_id_platform",
                table: "IM_CONFIG",
                columns: new[] { "instance_id", "platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IM_CONFIG_platform_status",
                table: "IM_CONFIG",
                columns: new[] { "platform", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_IM_CONFIG_tenant_id_owner_user_id",
                table: "IM_CONFIG",
                columns: new[] { "tenant_id", "owner_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IM_CONFIG");

            migrationBuilder.DropIndex(
                name: "IX_MESSAGE_channel_external_message_id",
                table: "MESSAGE");

            migrationBuilder.DropIndex(
                name: "IX_MESSAGE_instance_id_channel_created_at",
                table: "MESSAGE");

            migrationBuilder.DropColumn(name: "channel", table: "MESSAGE");
            migrationBuilder.DropColumn(name: "delivery_status", table: "MESSAGE");
            migrationBuilder.DropColumn(name: "error_message", table: "MESSAGE");
            migrationBuilder.DropColumn(name: "external_message_id", table: "MESSAGE");
            migrationBuilder.DropColumn(name: "external_user_id", table: "MESSAGE");
            migrationBuilder.DropColumn(name: "metadata_json", table: "MESSAGE");
        }
    }
}

