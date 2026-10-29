using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TranXuanThanh_2380614806_N7_API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOtpRateLimitIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OtpChallenges_UserId",
                table: "OtpChallenges");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_CreatedAtUtc",
                table: "OtpChallenges",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_UserId_CreatedAtUtc",
                table: "OtpChallenges",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OtpChallenges_CreatedAtUtc",
                table: "OtpChallenges");

            migrationBuilder.DropIndex(
                name: "IX_OtpChallenges_UserId_CreatedAtUtc",
                table: "OtpChallenges");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_UserId",
                table: "OtpChallenges",
                column: "UserId");
        }
    }
}
