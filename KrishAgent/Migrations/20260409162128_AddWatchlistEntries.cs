using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KrishAgent.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlistEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchlistEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ListType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchlistEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistEntries_ListType_SortOrder",
                table: "WatchlistEntries",
                columns: new[] { "ListType", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistEntries_ListType_Symbol",
                table: "WatchlistEntries",
                columns: new[] { "ListType", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchlistEntries");
        }
    }
}
