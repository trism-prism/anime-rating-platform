using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class EmailTwoFactorAndDropSecurityEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecurityEvents");

            // two-step verification now uses emailed codes: forget the old authenticator keys and recovery codes
            migrationBuilder.Sql(
                "DELETE FROM \"AspNetUserTokens\" WHERE \"LoginProvider\" = '[AspNetUserStore]' AND \"Name\" IN ('AuthenticatorKey', 'RecoveryCodes');");

            // an unconfirmed address cannot be trusted to receive the codes
            migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"TwoFactorEnabled\" = false WHERE \"EmailConfirmed\" = false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecurityEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Ip = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    UserAgent = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvents_UserId_At",
                table: "SecurityEvents",
                columns: new[] { "UserId", "At" });
        }
    }
}
