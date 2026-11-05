using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Everything that exists was written when all of it was private: it must not become visible retroactively.
            // The default backfills the old rows and is dropped again below, so new rows get the model's value (Public).
            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "WatchList",
                type: "text",
                nullable: false,
                defaultValue: "OnlyMe");

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "Ratings",
                type: "text",
                nullable: false,
                defaultValue: "OnlyMe");

            migrationBuilder.AddColumn<int>(
                name: "FunnyCount",
                table: "Ratings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HelpfulCount",
                table: "Ratings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "Ratings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Spoiler",
                table: "Ratings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "UnhelpfulCount",
                table: "Ratings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("ALTER TABLE \"WatchList\" ALTER COLUMN \"Audience\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"Ratings\" ALTER COLUMN \"Audience\" DROP DEFAULT;");
            // the review lists treat "has text" as ReviewText IS NOT NULL (their indexes are partial on it)
            migrationBuilder.Sql("UPDATE \"Ratings\" SET \"ReviewText\" = NULL WHERE btrim(\"ReviewText\") = '';");

            migrationBuilder.CreateTable(
                name: "Reactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RatingId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reactions_Ratings_RatingId",
                        column: x => x.RatingId,
                        principalTable: "Ratings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Replies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RatingId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Spoiler = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Replies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Replies_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Replies_Ratings_RatingId",
                        column: x => x.RatingId,
                        principalTable: "Ratings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_TitleId_Id",
                table: "Ratings",
                columns: new[] { "TitleId", "Id" },
                filter: "\"ReviewText\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_TitleId_Score_Id",
                table: "Ratings",
                columns: new[] { "TitleId", "Score", "Id" },
                filter: "\"ReviewText\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Ratings_UserId_Id",
                table: "Ratings",
                columns: new[] { "UserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ActorId_SubjectId",
                table: "Notifications",
                columns: new[] { "UserId", "ActorId", "SubjectId" },
                unique: true,
                filter: "\"Type\" = 'Reaction'");

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_RatingId_UserId",
                table: "Reactions",
                columns: new[] { "RatingId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_UserId",
                table: "Reactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Replies_RatingId_Id",
                table: "Replies",
                columns: new[] { "RatingId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Replies_UserId",
                table: "Replies",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reactions");

            migrationBuilder.DropTable(
                name: "Replies");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_TitleId_Id",
                table: "Ratings");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_TitleId_Score_Id",
                table: "Ratings");

            migrationBuilder.DropIndex(
                name: "IX_Ratings_UserId_Id",
                table: "Ratings");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_ActorId_SubjectId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "WatchList");

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "Ratings");

            migrationBuilder.DropColumn(
                name: "FunnyCount",
                table: "Ratings");

            migrationBuilder.DropColumn(
                name: "HelpfulCount",
                table: "Ratings");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "Ratings");

            migrationBuilder.DropColumn(
                name: "Spoiler",
                table: "Ratings");

            migrationBuilder.DropColumn(
                name: "UnhelpfulCount",
                table: "Ratings");
        }
    }
}
