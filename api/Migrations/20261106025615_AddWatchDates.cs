using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "FinishedOn",
                table: "WatchList",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RewatchCount",
                table: "WatchList",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartedOn",
                table: "WatchList",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinishedOn",
                table: "WatchList");

            migrationBuilder.DropColumn(
                name: "RewatchCount",
                table: "WatchList");

            migrationBuilder.DropColumn(
                name: "StartedOn",
                table: "WatchList");
        }
    }
}
