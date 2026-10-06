using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EZmatchApi.Migrations
{
    /// <inheritdoc />
    public partial class FixedBookingsAndActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancelled_by",
                table: "bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fixed_booking_id",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fixed_bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    club_id = table.Column<Guid>(type: "uuid", nullable: false),
                    court_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixed_bookings", x => x.id);
                    table.ForeignKey(
                        name: "fk_fixed_bookings_clubs_club_id",
                        column: x => x.club_id,
                        principalTable: "clubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fixed_bookings_courts_court_id",
                        column: x => x.court_id,
                        principalTable: "courts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fixed_bookings_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_fixed_booking_id_starts_at",
                table: "bookings",
                columns: new[] { "fixed_booking_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_fixed_bookings_club_id",
                table: "fixed_bookings",
                column: "club_id");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_bookings_court_id_day_of_week_start_time",
                table: "fixed_bookings",
                columns: new[] { "court_id", "day_of_week", "start_time" },
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_fixed_bookings_customer_id",
                table: "fixed_bookings",
                column: "customer_id");

            migrationBuilder.AddForeignKey(
                name: "fk_bookings_fixed_bookings_fixed_booking_id",
                table: "bookings",
                column: "fixed_booking_id",
                principalTable: "fixed_bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_bookings_fixed_bookings_fixed_booking_id",
                table: "bookings");

            migrationBuilder.DropTable(
                name: "fixed_bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_fixed_booking_id_starts_at",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "fixed_booking_id",
                table: "bookings");
        }
    }
}
