using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namerre.Infrastructure.Renaming.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParentId",
                table: "Documents",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            // Existing rollback runs only recorded their parent inside the JSON payload.
            migrationBuilder.Sql(
                "UPDATE \"Documents\" SET \"ParentId\" = json_extract(\"Json\", '$.parentRunId') WHERE \"Kind\" = 'Run';");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ParentId",
                table: "Documents",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_ParentId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Documents");
        }
    }
}
