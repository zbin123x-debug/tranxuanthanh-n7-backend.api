using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TranXuanThanh_2380614806_N7_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceMotionMagnitude : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MotionMagnitude",
                table: "Attendances",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MotionMagnitude",
                table: "Attendances");
        }
    }
}
