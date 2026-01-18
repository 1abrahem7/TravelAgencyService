using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel_Agency_Service.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifiedAtToWaitingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add NotifiedAt column to WaitingList table
            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedAt",
                table: "WaitingList",
                type: "datetime2",
                nullable: true);

            // Add WaitingListNotificationExpirationDays to AdminSettings table
            migrationBuilder.AddColumn<int>(
                name: "WaitingListNotificationExpirationDays",
                table: "AdminSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove WaitingListNotificationExpirationDays from AdminSettings table
            migrationBuilder.DropColumn(
                name: "WaitingListNotificationExpirationDays",
                table: "AdminSettings");

            // Remove NotifiedAt column from WaitingList table
            migrationBuilder.DropColumn(
                name: "NotifiedAt",
                table: "WaitingList");
        }
    }
}
