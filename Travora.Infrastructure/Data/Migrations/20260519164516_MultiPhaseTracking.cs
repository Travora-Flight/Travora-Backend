using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travora.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiPhaseTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderServiceId",
                table: "BaggagePhotos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaggagePhotos_OrderServiceId",
                table: "BaggagePhotos",
                column: "OrderServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_BaggagePhotos_OrderServices_OrderServiceId",
                table: "BaggagePhotos",
                column: "OrderServiceId",
                principalTable: "OrderServices",
                principalColumn: "OrderServiceId");

            // ===== DATA MIGRATION =====
            // Old enum: Pickup=1, AirportCheckin=2, Delivery=3
            // New enum: Pickup=1, DepartureCheckin=2, ArrivalCheckin=3, Delivery=4, Tracking=5
            //
            // AirportCheckin(2) → DepartureCheckin(2) = same value, no change needed
            // Delivery(3) → Delivery(4) = MUST be remapped

            // Step 1: Remap Delivery from old value (3) to new value (4) in PackageServices
            migrationBuilder.Sql(@"
                UPDATE PackageServices SET ExecutionPhase = 4 WHERE ExecutionPhase = 3;
            ");

            // Step 2: Seed ArrivalCheckin (phase=3) for Door To Door (PKG001)
            // Door To Door now needs 4 phases: Pickup(1), DepartureCheckin(2), ArrivalCheckin(3), Delivery(4)
            migrationBuilder.Sql(@"
                INSERT INTO PackageServices (PackageId, ServiceId, ExecutionPhase, IncludedInBase, CreatedAt)
                SELECT ps.PackageId, ps.ServiceId, 3, ps.IncludedInBase, GETUTCDATE()
                FROM PackageServices ps
                INNER JOIN Packages p ON ps.PackageId = p.PackageId
                WHERE p.PackageCode = 'PKG001' AND ps.ExecutionPhase = 2
                AND NOT EXISTS (
                    SELECT 1 FROM PackageServices x WHERE x.PackageId = ps.PackageId AND x.ExecutionPhase = 3
                );
            ");

            // Step 3: Seed BaggageOffice checkpoint if not exists
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM Checkpoints WHERE CheckpointType = 9)
                BEGIN
                    INSERT INTO Checkpoints (CheckpointName, CheckpointType, Description, SequenceOrder)
                    VALUES ('Lost Baggage Office', 9, 'Lost baggage office at airport', 0);
                END
            ");

            // Step 4: Remap BaggageTrackingStatus for new values
            // Old: Registered=1, PickedUp=2, AtSecurity=3, AtTerminal=4, AtGate=5,
            //      LoadedOnAircraft=6, Arrived=7, AtCustoms=8, OnBelt=9, OutForDelivery=10, Delivered=11, Cancelled=12
            // New: Registered=1, PickedUp=2, ArrivedAtAirport=3, AtSecurity=4, AtTerminal=5,
            //      AtGate=6, LoadedOnAircraft=7, Arrived=8, AtCustoms=9, OnBelt=10, AtBaggageOffice=11,
            //      OutForDelivery=12, Delivered=13, Cancelled=14
            migrationBuilder.Sql(@"
                -- Move all statuses to temp values (add 100) to avoid conflicts
                UPDATE BaggageTrackings SET Status = Status + 100 WHERE Status >= 3;

                -- Remap from temp: shift everything up by 1 to make room for ArrivedAtAirport(3)
                -- Old 3 (AtSecurity) → New 4
                UPDATE BaggageTrackings SET Status = 4 WHERE Status = 103;
                -- Old 4 (AtTerminal) → New 5
                UPDATE BaggageTrackings SET Status = 5 WHERE Status = 104;
                -- Old 5 (AtGate) → New 6
                UPDATE BaggageTrackings SET Status = 6 WHERE Status = 105;
                -- Old 6 (LoadedOnAircraft) → New 7
                UPDATE BaggageTrackings SET Status = 7 WHERE Status = 106;
                -- Old 7 (Arrived) → New 8
                UPDATE BaggageTrackings SET Status = 8 WHERE Status = 107;
                -- Old 8 (AtCustoms) → New 9
                UPDATE BaggageTrackings SET Status = 9 WHERE Status = 108;
                -- Old 9 (OnBelt) → New 10
                UPDATE BaggageTrackings SET Status = 10 WHERE Status = 109;
                -- Old 10 (OutForDelivery) → New 12 (skip 11 = AtBaggageOffice)
                UPDATE BaggageTrackings SET Status = 12 WHERE Status = 110;
                -- Old 11 (Delivered) → New 13
                UPDATE BaggageTrackings SET Status = 13 WHERE Status = 111;
                -- Old 12 (Cancelled) → New 14
                UPDATE BaggageTrackings SET Status = 14 WHERE Status = 112;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BaggagePhotos_OrderServices_OrderServiceId",
                table: "BaggagePhotos");

            migrationBuilder.DropIndex(
                name: "IX_BaggagePhotos_OrderServiceId",
                table: "BaggagePhotos");

            migrationBuilder.DropColumn(
                name: "OrderServiceId",
                table: "BaggagePhotos");
        }
    }
}
