using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorPayment.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBankTransferValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<string>(
                name: "TransactionReference",
                table: "BankTransferReceipts",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "TransferDateUtc",
                table: "BankTransferReceipts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransferredAmount",
                table: "BankTransferReceipts",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ValidationEventId",
                table: "BankTransferReceipts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropColumn(
                name: "TransactionReference",
                table: "BankTransferReceipts");

            migrationBuilder.DropColumn(
                name: "TransferDateUtc",
                table: "BankTransferReceipts");

            migrationBuilder.DropColumn(
                name: "TransferredAmount",
                table: "BankTransferReceipts");

            migrationBuilder.DropColumn(
                name: "ValidationEventId",
                table: "BankTransferReceipts");
        }
    }
}
