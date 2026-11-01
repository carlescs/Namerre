using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namerre.Infrastructure.Renaming.Migrations
{
    /// <inheritdoc />
    public partial class AddUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "NOCASE"),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Disabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Stamp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSignInAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Role", "\"Role\" IN ('Admin', 'User')");
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Issuer = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_Issuer_Subject",
                table: "UserLogins",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            // The single administrator becomes the first Admin user and keeps its password. Its stamp is new, so
            // sessions from before the upgrade sign in again.
            migrationBuilder.Sql("""
                INSERT INTO "Users" ("Id", "Username", "PasswordHash", "Role", "Disabled", "Stamp", "CreatedAt")
                SELECT "Id", "Username", "PasswordHash", 'Admin', 0, lower(hex(randomblob(16))),
                    strftime('%Y-%m-%d %H:%M:%S', 'now') || '+00:00'
                FROM "Administrators";
                """);

            migrationBuilder.DropTable(
                name: "Administrators");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Administrators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Administrators", x => x.Id);
                    table.CheckConstraint("CK_Administrators_Singleton", "\"Id\" = 1");
                });

            // Only one administrator survives a downgrade: the oldest enabled Admin with a password.
            migrationBuilder.Sql("""
                INSERT INTO "Administrators" ("Id", "Username", "PasswordHash")
                SELECT 1, "Username", "PasswordHash" FROM "Users"
                WHERE "Role" = 'Admin' AND "Disabled" = 0 AND "PasswordHash" IS NOT NULL
                ORDER BY "Id" LIMIT 1;
                """);

            migrationBuilder.DropTable(
                name: "UserLogins");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
