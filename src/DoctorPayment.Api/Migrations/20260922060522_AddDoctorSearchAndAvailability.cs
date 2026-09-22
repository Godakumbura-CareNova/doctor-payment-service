using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorPayment.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorSearchAndAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "DoctorProfiles",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DoctorAvailabilitySlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DoctorProfileId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsBooked = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorAvailabilitySlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorAvailabilitySlots_DoctorProfiles_DoctorProfileId",
                        column: x => x.DoctorProfileId,
                        principalTable: "DoctorProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorProfiles_FullName",
                table: "DoctorProfiles",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorProfiles_Specialization",
                table: "DoctorProfiles",
                column: "Specialization");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorAvailabilitySlots_DoctorProfileId_StartUtc_IsBooked",
                table: "DoctorAvailabilitySlots",
                columns: new[] { "DoctorProfileId", "StartUtc", "IsBooked" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorAvailabilitySlots");

            migrationBuilder.DropIndex(
                name: "IX_DoctorProfiles_FullName",
                table: "DoctorProfiles");

            migrationBuilder.DropIndex(
                name: "IX_DoctorProfiles_Specialization",
                table: "DoctorProfiles");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "DoctorProfiles");
        }
    }
}
