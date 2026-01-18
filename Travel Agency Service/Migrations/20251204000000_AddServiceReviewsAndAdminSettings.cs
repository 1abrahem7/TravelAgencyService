using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel_Agency_Service.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceReviewsAndAdminSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add DiscountExpiryDate to Trips table
            migrationBuilder.AddColumn<DateTime>(
                name: "DiscountExpiryDate",
                table: "Trips",
                type: "datetime2",
                nullable: true);

            // Create ServiceReviews table
            migrationBuilder.CreateTable(
                name: "ServiceReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceReviews_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceReviews_UserId",
                table: "ServiceReviews",
                column: "UserId");

            // Create AdminSettings table
            migrationBuilder.CreateTable(
                name: "AdminSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DaysBeforeTripLatestBooking = table.Column<int>(type: "int", nullable: false),
                    DaysBeforeTripCancellationDeadline = table.Column<int>(type: "int", nullable: false),
                    DaysBeforeTripReminder = table.Column<int>(type: "int", nullable: false),
                    MaxDiscountDurationDays = table.Column<int>(type: "int", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceReviews");

            migrationBuilder.DropTable(
                name: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "DiscountExpiryDate",
                table: "Trips");
        }
    }
}
