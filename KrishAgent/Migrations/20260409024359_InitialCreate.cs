using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KrishAgent.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    AlertType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Threshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsTriggered = table.Column<bool>(type: "INTEGER", nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalysisHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    RSI = table.Column<decimal>(type: "TEXT", nullable: true),
                    MACD_Line = table.Column<decimal>(type: "TEXT", nullable: true),
                    Bollinger_Upper = table.Column<decimal>(type: "TEXT", nullable: true),
                    MA20 = table.Column<decimal>(type: "TEXT", nullable: true),
                    Trend = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Confidence = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    AIModel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioPositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    EntryDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StopLoss = table.Column<decimal>(type: "TEXT", nullable: true),
                    TakeProfit = table.Column<decimal>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioPositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Open = table.Column<decimal>(type: "TEXT", nullable: false),
                    High = table.Column<decimal>(type: "TEXT", nullable: false),
                    Low = table.Column<decimal>(type: "TEXT", nullable: false),
                    Close = table.Column<decimal>(type: "TEXT", nullable: false),
                    Volume = table.Column<long>(type: "INTEGER", nullable: false),
                    RSI = table.Column<decimal>(type: "TEXT", nullable: true),
                    MACD_Line = table.Column<decimal>(type: "TEXT", nullable: true),
                    MACD_Signal = table.Column<decimal>(type: "TEXT", nullable: true),
                    MACD_Histogram = table.Column<decimal>(type: "TEXT", nullable: true),
                    Bollinger_Upper = table.Column<decimal>(type: "TEXT", nullable: true),
                    Bollinger_Middle = table.Column<decimal>(type: "TEXT", nullable: true),
                    Bollinger_Lower = table.Column<decimal>(type: "TEXT", nullable: true),
                    MA20 = table.Column<decimal>(type: "TEXT", nullable: true),
                    MA50 = table.Column<decimal>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockPrices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Side = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    EntryDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    ExitDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Pnl = table.Column<decimal>(type: "TEXT", nullable: true),
                    PnlPercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    ExitReason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Symbol_IsActive",
                table: "Alerts",
                columns: new[] { "Symbol", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisHistory_Symbol_Date",
                table: "AnalysisHistory",
                columns: new[] { "Symbol", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_StockPrices_Symbol_Date",
                table: "StockPrices",
                columns: new[] { "Symbol", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "AnalysisHistory");

            migrationBuilder.DropTable(
                name: "PortfolioPositions");

            migrationBuilder.DropTable(
                name: "StockPrices");

            migrationBuilder.DropTable(
                name: "Trades");
        }
    }
}
