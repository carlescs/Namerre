using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namerre.Infrastructure.Renaming.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RevokedSessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresAtUnix = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevokedSessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "SessionEpochs",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Epoch = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionEpochs", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RevokedSessions_ExpiresAtUnix",
                table: "RevokedSessions",
                column: "ExpiresAtUnix");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RevokedSessions");

            migrationBuilder.DropTable(
                name: "SessionEpochs");
        }
    }
}
