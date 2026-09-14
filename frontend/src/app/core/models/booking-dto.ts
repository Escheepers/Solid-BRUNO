/**
 * The exact string set `BookingDto.Status` serializes as (`JsonStringEnumConverter`,
 * Program.cs -- the first enum ever exposed on this API's wire shape). `Completed`
 * is never directly settable by a user in any form (`EXPERIENCE.md`: system-derived,
 * AD-17) -- it only ever arrives on a `BookingDto` read from the server.
 */
export type BookingStatusDto = 'Active' | 'Completed' | 'Cancelled';

/**
 * Raw wire-format DTO, matching `BookingDto` in
 * `src/BrunoVehicleHire.Application/Bookings/Dtos/BookingDto.cs` exactly (camelCase,
 * `startDate`/`endDate` as `DateOnly` ISO date strings, `createdDate` as a full ISO
 * datetime string). Denormalized with the Vehicle/Customer display fields the
 * Bookings list needs (spec-4-1's Boundaries) rather than navigation properties --
 * mirrors the backend DTO's own reasoning. Never used directly by feature
 * components — `features/bookings/models/booking.ts`'s `toBooking` mapper converts
 * it to the feature's own `Booking` view model first, per the frontend
 * model-separation convention (core DTOs vs. feature view models are never used
 * interchangeably).
 */
export interface BookingDto {
  id: string;
  vehicleId: string;
  vehicleMake: string;
  vehicleModel: string;
  vehicleRegistrationNumber: string;
  customerId: string;
  customerFirstName: string;
  customerLastName: string;
  customerIsAnonymized: boolean;
  startDate: string;
  endDate: string;
  totalPrice: number;
  status: BookingStatusDto;
  createdDate: string;
}
