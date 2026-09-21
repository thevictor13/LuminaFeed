using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LuminaFeed.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Feeds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FeedUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SiteUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Popularity = table.Column<int>(type: "INTEGER", nullable: false),
                    ETag = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastModified = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LastPolledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feeds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Feeds_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FeedId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Link = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Articles_Feeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "Feeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    FeedId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SlackEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SlackWebhookUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Feeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "Feeds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Articles_FeedId_ExternalId",
                table: "Articles",
                columns: new[] { "FeedId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Feeds_CategoryId_Popularity",
                table: "Feeds",
                columns: new[] { "CategoryId", "Popularity" });

            migrationBuilder.CreateIndex(
                name: "IX_Feeds_FeedUrl",
                table: "Feeds",
                column: "FeedUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_FeedId",
                table: "Subscriptions",
                column: "FeedId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId_FeedId",
                table: "Subscriptions",
                columns: new[] { "UserId", "FeedId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Articles");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Feeds");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "AspNetUsers");
        }
    }
}
