using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoctorPayment.Api.Migrations
{
    /// <summary>
    /// Payments chosen before the customer name was kept on the payment have an empty name. Fill it from the payment's
    /// bank transfer receipts, then from any other payment or receipt of the same patient. Data only; no schema change.
    /// </summary>
    public partial class BackfillMedicinePaymentCustomerNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. From this payment's own receipts ("A patient" is the placeholder used when the token had no name).
            migrationBuilder.Sql(@"
UPDATE `MedicinePayments` AS p
JOIN (
    SELECT * FROM (
        SELECT `MedicinePaymentId`, MAX(`CustomerName`) AS `Name`
        FROM `BankTransferReceipts`
        WHERE `CustomerName` <> '' AND `CustomerName` <> 'A patient'
        GROUP BY `MedicinePaymentId`
    ) AS names
) AS r ON r.`MedicinePaymentId` = p.`Id`
SET p.`CustomerName` = r.`Name`
WHERE p.`CustomerName` = '';");

            // 2. From the same patient's other payments or receipts (the inner SELECT is materialised so MySQL allows it).
            migrationBuilder.Sql(@"
UPDATE `MedicinePayments` AS p
JOIN (
    SELECT `PatientId`, MAX(`Name`) AS `Name` FROM (
        SELECT `PatientId`, `CustomerName` AS `Name` FROM `MedicinePayments` WHERE `CustomerName` <> ''
        UNION ALL
        SELECT `PatientId`, `CustomerName` FROM `BankTransferReceipts` WHERE `CustomerName` <> '' AND `CustomerName` <> 'A patient'
    ) AS known
    GROUP BY `PatientId`
) AS n ON n.`PatientId` = p.`PatientId`
SET p.`CustomerName` = n.`Name`
WHERE p.`CustomerName` = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Filled names are correct data; nothing to undo.
        }
    }
}
