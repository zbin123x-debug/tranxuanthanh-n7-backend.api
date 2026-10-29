using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TranXuanThanh_2380614806_N7_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledAttendanceQrSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceQrSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceQrSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceQrSessions_StartsAtUtc_EndsAtUtc",
                table: "AttendanceQrSessions",
                columns: new[] { "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceQrSessions_TokenHash",
                table: "AttendanceQrSessions",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceQrSessions");
        }
    }
}
