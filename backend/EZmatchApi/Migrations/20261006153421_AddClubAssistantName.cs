using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EZmatchApi.Migrations
{
    /// <inheritdoc />
    public partial class AddClubAssistantName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assistant_name",
                table: "clubs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assistant_name",
                table: "clubs");
        }
    }
}
