using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiPhaseCarServiceSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderServiceId",
                table: "QrScans",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QrScans_OrderServiceId",
                table: "QrScans",
                column: "OrderServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_QrScans_OrderServices_OrderServiceId",
                table: "QrScans",
                column: "OrderServiceId",
                principalTable: "OrderServices",
                principalColumn: "OrderServiceId",
                onDelete: ReferentialAction.Restrict);

            // ================================================================
            // DATA MIGRATION: Remap BaggageTrackingStatus enum values
            // ================================================================
            // Old order: Registered=1, PickedUp=2, AtCustoms=3, AtSecurity=4,
            //            AtTerminal=5, AtGate=6, LoadedOnAircraft=7, Arrived=8,
            //            OnBelt=9, OutForDelivery=10, Delivered=11, Cancelled=12
            //
            // New order: Registered=1, PickedUp=2, AtSecurity=3, AtTerminal=4,
            //            AtGate=5, LoadedOnAircraft=6, Arrived=7, AtCustoms=8,
            //            OnBelt=9, OutForDelivery=10, Delivered=11, Cancelled=12
            // ================================================================
            migrationBuilder.Sql(@"
                -- Step 1: Move AtCustoms (3) to temp value 99 to avoid conflicts
                UPDATE BaggageTrackings SET Status = 99 WHERE Status = 3;

                -- Step 2: Shift remaining values down by 1 (4→3, 5→4, 6→5, 7→6, 8→7)
                UPDATE BaggageTrackings SET Status = 3 WHERE Status = 4;
                UPDATE BaggageTrackings SET Status = 4 WHERE Status = 5;
                UPDATE BaggageTrackings SET Status = 5 WHERE Status = 6;
                UPDATE BaggageTrackings SET Status = 6 WHERE Status = 7;
                UPDATE BaggageTrackings SET Status = 7 WHERE Status = 8;

                -- Step 3: Place AtCustoms at its new position (99→8)
                UPDATE BaggageTrackings SET Status = 8 WHERE Status = 99;
            ");

            // ================================================================
            // DATA MIGRATION: Add AirportCheckin PackageService for Car Service packages
            // ================================================================
            // Find the AirportCheckin service (ServiceId) from Door To Door package
            // and add it to Car Service To Airport and Car Service From Airport
            // ================================================================
            migrationBuilder.Sql(@"
                DECLARE @AirportCheckinServiceId INT;
                DECLARE @CarToAirportPackageId INT;
                DECLARE @CarFromAirportPackageId INT;

                -- Get the ServiceId used for AirportCheckin in Door To Door
                SELECT @AirportCheckinServiceId = ps.ServiceId
                FROM PackageServices ps
                INNER JOIN Packages p ON ps.PackageId = p.PackageId
                WHERE p.PackageCode = 'PKG001'
                  AND ps.ExecutionPhase = 2; -- AirportCheckin

                -- Get Car Service To Airport PackageId
                SELECT @CarToAirportPackageId = PackageId
                FROM Packages WHERE PackageCode = 'PKG002';

                -- Get Car Service From Airport PackageId
                SELECT @CarFromAirportPackageId = PackageId
                FROM Packages WHERE PackageCode = 'PKG003';

                -- Add AirportCheckin to Car Service To Airport (if not exists)
                IF @AirportCheckinServiceId IS NOT NULL AND @CarToAirportPackageId IS NOT NULL
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM PackageServices
                        WHERE PackageId = @CarToAirportPackageId
                          AND ServiceId = @AirportCheckinServiceId
                    )
                    BEGIN
                        INSERT INTO PackageServices (IncludedInBase, ExecutionPhase, CreatedAt, PackageId, ServiceId)
                        VALUES (1, 2, GETUTCDATE(), @CarToAirportPackageId, @AirportCheckinServiceId);
                    END
                END

                -- Add AirportCheckin to Car Service From Airport (if not exists)
                IF @AirportCheckinServiceId IS NOT NULL AND @CarFromAirportPackageId IS NOT NULL
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM PackageServices
                        WHERE PackageId = @CarFromAirportPackageId
                          AND ServiceId = @AirportCheckinServiceId
                    )
                    BEGIN
                        INSERT INTO PackageServices (IncludedInBase, ExecutionPhase, CreatedAt, PackageId, ServiceId)
                        VALUES (1, 2, GETUTCDATE(), @CarFromAirportPackageId, @AirportCheckinServiceId);
                    END
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QrScans_OrderServices_OrderServiceId",
                table: "QrScans");

            migrationBuilder.DropIndex(
                name: "IX_QrScans_OrderServiceId",
                table: "QrScans");

            migrationBuilder.DropColumn(
                name: "OrderServiceId",
                table: "QrScans");
        }
    }
}
