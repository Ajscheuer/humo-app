using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humo.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Analytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CookAnalytics",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeatType = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DurationHours = table.Column<double>(type: "float", nullable: true),
                    TimePerKg = table.Column<double>(type: "float", nullable: true),
                    StallStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StallEndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StallHours = table.Column<double>(type: "float", nullable: true),
                    PitStabilityScore = table.Column<double>(type: "float", nullable: true),
                    FuelEfficiency = table.Column<double>(type: "float", nullable: true),
                    IsEstimated = table.Column<bool>(type: "bit", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookAnalytics", x => new { x.AccountId, x.CookId });
                });

            migrationBuilder.CreateTable(
                name: "UserBaselines",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeatType = table.Column<int>(type: "int", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: false),
                    SampleSize = table.Column<int>(type: "int", nullable: false),
                    Mean = table.Column<double>(type: "float", nullable: false),
                    StandardDeviation = table.Column<double>(type: "float", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBaselines", x => new { x.AccountId, x.MeatType, x.EquipmentId, x.Metric });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CookAnalytics_AccountId_MeatType_EquipmentId",
                table: "CookAnalytics",
                columns: new[] { "AccountId", "MeatType", "EquipmentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CookAnalytics");

            migrationBuilder.DropTable(
                name: "UserBaselines");
        }
    }
}
