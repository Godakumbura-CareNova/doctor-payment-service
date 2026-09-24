using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorPayment.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConsultationFeeChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsultationFeeChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DoctorProfileId = table.Column<int>(type: "int", nullable: false),
                    CurrentFee = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    RequestedFee = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PendingDoctorProfileId = table.Column<int>(type: "int", nullable: true),
                    RejectionReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewedByAdminId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultationFeeChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsultationFeeChangeRequests_DoctorProfiles_DoctorProfileId",
                        column: x => x.DoctorProfileId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DoctorNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DoctorProfileId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelatedEntityType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelatedEntityId = table.Column<int>(type: "int", nullable: true),
                    IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorNotifications_DoctorProfiles_DoctorProfileId",
                        column: x => x.DoctorProfileId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationFeeChangeRequests_DoctorProfileId_SubmittedAtUtc",
                table: "ConsultationFeeChangeRequests",
                columns: new[] { "DoctorProfileId", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationFeeChangeRequests_PendingDoctorProfileId",
                table: "ConsultationFeeChangeRequests",
                column: "PendingDoctorProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationFeeChangeRequests_Status_SubmittedAtUtc",
                table: "ConsultationFeeChangeRequests",
                columns: new[] { "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorNotifications_DoctorProfileId_CreatedAtUtc",
                table: "DoctorNotifications",
                columns: new[] { "DoctorProfileId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsultationFeeChangeRequests");

            migrationBuilder.DropTable(
                name: "DoctorNotifications");
        }
    }
}
