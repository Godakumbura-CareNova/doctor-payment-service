using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorPayment.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExperienceOther : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Other",
                table: "DoctorExperiences",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Other",
                table: "DoctorExperiences");
        }
    }
}
