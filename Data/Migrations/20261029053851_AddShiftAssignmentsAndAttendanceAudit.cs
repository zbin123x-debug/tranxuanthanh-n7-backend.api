using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TranXuanThanh_2380614806_N7_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftAssignmentsAndAttendanceAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceId = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ChangeReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceAuditLogs_Attendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "Attendances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeShifts_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeShifts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAnomalies_AttendanceId",
                table: "AttendanceAnomalies",
                column: "AttendanceId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAnomalies_UserId",
                table: "AttendanceAnomalies",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceAuditLogs_AttendanceId",
                table: "AttendanceAuditLogs",
                column: "AttendanceId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShifts_ShiftId",
                table: "EmployeeShifts",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShifts_UserId_WorkDate_ShiftId",
                table: "EmployeeShifts",
                columns: new[] { "UserId", "WorkDate", "ShiftId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceAnomalies_Attendances_AttendanceId",
                table: "AttendanceAnomalies",
                column: "AttendanceId",
                principalTable: "Attendances",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceAnomalies_Users_UserId",
                table: "AttendanceAnomalies",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceAnomalies_Attendances_AttendanceId",
                table: "AttendanceAnomalies");

            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceAnomalies_Users_UserId",
                table: "AttendanceAnomalies");

            migrationBuilder.DropTable(
                name: "AttendanceAuditLogs");

            migrationBuilder.DropTable(
                name: "EmployeeShifts");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceAnomalies_AttendanceId",
                table: "AttendanceAnomalies");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceAnomalies_UserId",
                table: "AttendanceAnomalies");
        }
    }
}
