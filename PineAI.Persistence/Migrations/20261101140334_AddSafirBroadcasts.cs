using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PineAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSafirBroadcasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SafirBroadcast",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelRequested = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafirBroadcast", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SafirBroadcastImage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BroadcastId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafirBroadcastImage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafirBroadcastImage_SafirBroadcast_BroadcastId",
                        column: x => x.BroadcastId,
                        principalTable: "SafirBroadcast",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SafirBroadcastLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BroadcastId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafirBroadcastLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafirBroadcastLog_SafirBroadcast_BroadcastId",
                        column: x => x.BroadcastId,
                        principalTable: "SafirBroadcast",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SafirBroadcastRecipient",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BroadcastId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafirBroadcastRecipient", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafirBroadcastRecipient_SafirBroadcast_BroadcastId",
                        column: x => x.BroadcastId,
                        principalTable: "SafirBroadcast",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SafirBroadcast_State_CreatedAt",
                table: "SafirBroadcast",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SafirBroadcastImage_BroadcastId",
                table: "SafirBroadcastImage",
                column: "BroadcastId");

            migrationBuilder.CreateIndex(
                name: "IX_SafirBroadcastLog_BroadcastId_Id",
                table: "SafirBroadcastLog",
                columns: new[] { "BroadcastId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SafirBroadcastRecipient_BroadcastId_Phone",
                table: "SafirBroadcastRecipient",
                columns: new[] { "BroadcastId", "Phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SafirBroadcastRecipient_BroadcastId_Status",
                table: "SafirBroadcastRecipient",
                columns: new[] { "BroadcastId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SafirBroadcastImage");

            migrationBuilder.DropTable(
                name: "SafirBroadcastLog");

            migrationBuilder.DropTable(
                name: "SafirBroadcastRecipient");

            migrationBuilder.DropTable(
                name: "SafirBroadcast");
        }
    }
}
