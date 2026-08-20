using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wasta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Localization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "language",
                table: "user_account",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "localized_text",
                columns: table => new
                {
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<long>(type: "bigint", nullable: false),
                    field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    language = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_localized_text", x => new { x.entity_type, x.entity_id, x.field, x.language });
                });

            migrationBuilder.CreateIndex(
                name: "ix_localized_text_language",
                table: "localized_text",
                column: "language");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "localized_text");

            migrationBuilder.DropColumn(
                name: "language",
                table: "user_account");
        }
    }
}
