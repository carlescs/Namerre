using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namerre.Infrastructure.Renaming.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchedFiles",
                columns: table => new
                {
                    Folder = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PlanId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedFiles", x => new { x.Folder, x.Path });
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFiles_Folder_Status",
                table: "WatchedFiles",
                columns: new[] { "Folder", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchedFiles");
        }
    }
}
