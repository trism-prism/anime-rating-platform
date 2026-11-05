using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Ratings_UserId_TitleId",
                table: "Ratings");

            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "Titles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Titles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                table: "Titles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "RatingSum",
                table: "Titles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Titles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SeriesLocked",
                table: "Titles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "StartDay",
                table: "Titles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartMonth",
                table: "Titles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Volumes",
                table: "Titles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "Titles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Volume",
                table: "Ratings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    CoverUrl = table.Column<string>(type: "text", nullable: true),
                    Popularity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TitleRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromAniListId = table.Column<int>(type: "integer", nullable: false),
                    ToAniListId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleRelations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Titles_SeriesId_Position",
                table: "Titles",
                columns: new[] { "SeriesId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_UserId_TitleId",
                table: "Ratings",
                columns: new[] { "UserId", "TitleId" },
                unique: true,
                filter: "\"Volume\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_UserId_TitleId_Volume",
                table: "Ratings",
                columns: new[] { "UserId", "TitleId", "Volume" },
                unique: true,
                filter: "\"Volume\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Series_Popularity_Id",
                table: "Series",
                columns: new[] { "Popularity", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TitleRelations_FromAniListId_ToAniListId_Type",
                table: "TitleRelations",
                columns: new[] { "FromAniListId", "ToAniListId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TitleRelations_ToAniListId",
                table: "TitleRelations",
                column: "ToAniListId");

            // existing titles get a series of their own (same id) so the foreign key holds; the next import regroups them
            migrationBuilder.Sql("""
                INSERT INTO "Series" ("Id", "Name", "Kind", "CoverUrl", "Popularity") OVERRIDING SYSTEM VALUE
                SELECT "Id", "Name", "Type", "CoverUrl", "Popularity" FROM "Titles";
                UPDATE "Titles" SET "SeriesId" = "Id";
                SELECT setval(pg_get_serial_sequence('"Series"', 'Id'), COALESCE(MAX("Id"), 1), COUNT(*) > 0) FROM "Series";
                """);

            // every rating that exists so far is a whole-title rating; carry it into the stored totals
            migrationBuilder.Sql("""
                UPDATE "Titles" t SET "RatingCount" = r.n, "RatingSum" = r.total
                FROM (SELECT "TitleId", COUNT(*)::int AS n, SUM("Overall") AS total FROM "Ratings" GROUP BY "TitleId") r
                WHERE r."TitleId" = t."Id";
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Titles_Series_SeriesId",
                table: "Titles",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // volume ratings cannot exist without the Volume column, and would break the unique index below
            migrationBuilder.Sql("DELETE FROM \"Ratings\" WHERE \"Volume\" IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Titles_Series_SeriesId",
                table: "Titles");

            migrationBuilder.DropTable(
                name: "Series");

            migrationBuilder.DropTable(
                name: "TitleRelations");

            migrationBuilder.DropIndex(
                name: "IX_Titles_SeriesId_Position",
                table: "Titles");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_UserId_TitleId",
                table: "Ratings");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_UserId_TitleId_Volume",
                table: "Ratings");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "RatingSum",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "SeriesLocked",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "StartDay",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "StartMonth",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "Volumes",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "Titles");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "Ratings");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_UserId_TitleId",
                table: "Ratings",
                columns: new[] { "UserId", "TitleId" },
                unique: true);
        }
    }
}
