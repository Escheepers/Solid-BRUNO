import { BookingStatusDto } from './booking-dto';

/**
 * Raw wire-format DTO, matching `CustomerSummaryDto` in
 * `src/BrunoVehicleHire.Application/CustomerSummaries/Dtos/CustomerSummaryDto.cs` exactly
 * (camelCase). Never used directly by feature components —
 * `features/customer-summary/models/customer-summary.ts`'s `toCustomerSummary` mapper
 * converts it to the feature's own view model first, per the frontend model-separation
 * convention (core DTOs vs. feature view models are never used interchangeably).
 * Deliberately carries no `isDeleted` field (spec-5-1's Scope decision 5 — this is a
 * customer-facing reference document, not an admin view).
 */
export interface CustomerSummaryDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  isAnonymized: boolean;
  bookings: CustomerSummaryBookingDto[];
}

/**
 * One row of the Customer Summary page's booking-history list — matching
 * `CustomerSummaryBookingDto` in the same backend file exactly. Denormalized with the
 * Vehicle display fields the read-only table needs, mirroring `BookingDto`'s own
 * denormalization reasoning (AD-1: Booking itself carries no navigation properties).
 * Deliberately carries no Customer fields — this only ever appears nested inside a
 * single `CustomerSummaryDto` that already carries them once (DRY).
 */
export interface CustomerSummaryBookingDto {
  id: string;
  vehicleMake: string;
  vehicleModel: string;
  vehicleRegistrationNumber: string;
  startDate: string;
  endDate: string;
  totalPrice: number;
  status: BookingStatusDto;
}
