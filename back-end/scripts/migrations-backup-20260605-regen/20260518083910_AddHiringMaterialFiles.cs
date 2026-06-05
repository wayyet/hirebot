using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddHiringMaterialFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hiring_material_files",
                columns: table => new
                {
                    material_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hire_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    relative_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_category_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    tenant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    operator_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    uploaded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hiring_material_files", x => x.material_file_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hiring_material_files_hire_id_session_id_uploaded_at_utc",
                table: "hiring_material_files",
                columns: new[] { "hire_id", "session_id", "uploaded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_hiring_material_files_session_id_relative_path",
                table: "hiring_material_files",
                columns: new[] { "session_id", "relative_path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hiring_material_files_sha256",
                table: "hiring_material_files",
                column: "sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hiring_material_files");
        }
    }
}
