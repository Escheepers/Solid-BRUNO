/**
 * Raw wire-format DTO, matching `VehicleDto` in
 * `src/BrunoVehicleHire.Application/Vehicles/Dtos/VehicleDto.cs` exactly (camelCase,
 * `createdDate` as an ISO date string). Never used directly by feature components —
 * `features/vehicles/models/vehicle.ts`'s `toVehicle` mapper converts it to the
 * feature's own `Vehicle` view model first, per the frontend model-separation
 * convention (core DTOs vs. feature view models are never used interchangeably).
 */
export interface VehicleDto {
  id: string;
  registrationNumber: string;
  make: string;
  model: string;
  year: number;
  dailyRate: number;
  createdDate: string;
}
