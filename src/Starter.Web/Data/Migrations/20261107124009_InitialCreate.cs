using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Starter.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "web");

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "web",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                schema: "web",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Ticket = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Tokens = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExpiresAt",
                schema: "web",
                table: "Sessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_UserId",
                schema: "web",
                table: "Sessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "web");

            migrationBuilder.DropTable(
                name: "Sessions",
                schema: "web");
        }
    }
}
