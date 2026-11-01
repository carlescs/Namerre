using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namerre.Infrastructure.Renaming.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "OwnerId",
                table: "Documents",
                type: "INTEGER",
                nullable: true);

            // A plan belongs to the user who prepared it; every run (renames and their rollbacks) belongs to the
            // owner of its plan. Watch-folder plans and history from before users stay without an owner.
            migrationBuilder.Sql(
                "UPDATE \"Documents\" SET \"OwnerId\" = json_extract(\"Json\", '$.createdBy.id') WHERE \"Kind\" = 'Plan';");
            migrationBuilder.Sql(
                "UPDATE \"Documents\" SET \"OwnerId\" = (SELECT \"p\".\"OwnerId\" FROM \"Documents\" AS \"p\" "
                + "WHERE \"p\".\"Kind\" = 'Plan' AND \"p\".\"Id\" = json_extract(\"Documents\".\"Json\", '$.planId')) "
                + "WHERE \"Kind\" = 'Run';");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Kind_OwnerId_Order",
                table: "Documents",
                columns: new[] { "Kind", "OwnerId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_Kind_OwnerId_Order",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Documents");
        }
    }
}
