using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class InstanceRuntimeChat_v1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "INSTANCE",
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
                    table.PrimaryKey("PK_INSTANCE", x => x.instance_id);
                });

            migrationBuilder.CreateTable(
                name: "CONVERSATION",
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
                    table.PrimaryKey("PK_CONVERSATION", x => x.conversation_id);
                });

            migrationBuilder.CreateTable(
                name: "MESSAGE",
                columns: table => new
                {
                    message_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MESSAGE", x => x.message_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_INSTANCE_based_on_template_id",
                table: "INSTANCE",
                column: "based_on_template_id");

            migrationBuilder.CreateIndex(
                name: "IX_INSTANCE_from_instance_id",
                table: "INSTANCE",
                column: "from_instance_id");

            migrationBuilder.CreateIndex(
                name: "IX_INSTANCE_owner_user_id_instance_type_status",
                table: "INSTANCE",
                columns: new[] { "owner_user_id", "instance_type", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_INSTANCE_tenant_id_department_id_instance_type_status",
                table: "INSTANCE",
                columns: new[] { "tenant_id", "department_id", "instance_type", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATION_instance_id_owner_user_id_channel",
                table: "CONVERSATION",
                columns: new[] { "instance_id", "owner_user_id", "channel" });

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATION_tenant_id_updated_at",
                table: "CONVERSATION",
                columns: new[] { "tenant_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGE_conversation_id_created_at",
                table: "MESSAGE",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGE_instance_id_created_at",
                table: "MESSAGE",
                columns: new[] { "instance_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MESSAGE");
            migrationBuilder.DropTable(name: "CONVERSATION");
            migrationBuilder.DropTable(name: "INSTANCE");
        }
    }
}
