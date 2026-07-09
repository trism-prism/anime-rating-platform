using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // no code is asked at login any more; switch the flag off for accounts that had it on
            migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"TwoFactorEnabled\" = false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // nothing to restore: who had it on is not kept
        }
    }
}
