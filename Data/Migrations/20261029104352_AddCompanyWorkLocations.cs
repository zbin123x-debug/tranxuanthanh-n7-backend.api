using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TranXuanThanh_2380614806_N7_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyWorkLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkLocationName",
                table: "Attendances",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyWorkLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    RadiusMeters = table.Column<double>(type: "float", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyWorkLocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyWorkLocations_IsActive_Name",
                table: "CompanyWorkLocations",
                columns: new[] { "IsActive", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyWorkLocations");

            migrationBuilder.DropColumn(
                name: "WorkLocationName",
                table: "Attendances");
        }
    }
}
