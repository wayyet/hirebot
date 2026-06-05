using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HireBot.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationWorkspaceStatePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvaluationWorkspaceStates",
                columns: table => new
                {
                    OwnerSubject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationWorkspaceStates", x => new { x.OwnerSubject, x.EmployeeId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationWorkspaceStates_UpdatedAtUtc",
                table: "EvaluationWorkspaceStates",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationWorkspaceStates");
        }
    }
}
