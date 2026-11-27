using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TapeCraft.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "spotify_user_id",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "access_token",
                table: "tokens",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "tokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "refresh_token",
                table: "tokens",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_users_spotify_user_id",
                table: "users",
                column: "spotify_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tokens_user_id",
                table: "tokens",
                column: "user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_tokens_users_user_id",
                table: "tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tokens_users_user_id",
                table: "tokens");

            migrationBuilder.DropIndex(
                name: "ix_users_spotify_user_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_tokens_user_id",
                table: "tokens");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "spotify_user_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "access_token",
                table: "tokens");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "tokens");

            migrationBuilder.DropColumn(
                name: "refresh_token",
                table: "tokens");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "tokens");
        }
    }
}
