using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EZmatchApi.Migrations
{
    /// <inheritdoc />
    public partial class BotShowsPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "bot_shows_prices",
                table: "clubs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bot_shows_prices",
                table: "clubs");
        }
    }
}
