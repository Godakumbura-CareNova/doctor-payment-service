using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorPayment.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBankTransferFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FlagReason",
                table: "BankTransferReceipts");

            migrationBuilder.DropColumn(
                name: "FlaggedAtUtc",
                table: "BankTransferReceipts");

            migrationBuilder.DropColumn(
                name: "FlaggedBy",
                table: "BankTransferReceipts");

            migrationBuilder.DropColumn(
                name: "IsFlagged",
                table: "BankTransferReceipts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FlagReason",
                table: "BankTransferReceipts",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "FlaggedAtUtc",
                table: "BankTransferReceipts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FlaggedBy",
                table: "BankTransferReceipts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<bool>(
                name: "IsFlagged",
                table: "BankTransferReceipts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
