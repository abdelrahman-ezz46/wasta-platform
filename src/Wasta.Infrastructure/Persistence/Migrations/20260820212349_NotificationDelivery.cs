using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "notification",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "channel",
                table: "notification",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "delivery_state",
                table: "notification",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatched_at",
                table: "notification",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error",
                table: "notification",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_state_created_at",
                table: "notification",
                columns: new[] { "delivery_state", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_delivery_state_created_at",
                table: "notification");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "notification");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "notification");

            migrationBuilder.DropColumn(
                name: "delivery_state",
                table: "notification");

            migrationBuilder.DropColumn(
                name: "dispatched_at",
                table: "notification");

            migrationBuilder.DropColumn(
                name: "last_error",
                table: "notification");
        }
    }
}
