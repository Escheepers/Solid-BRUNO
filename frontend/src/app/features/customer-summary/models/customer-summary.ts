import { BookingStatusDto } from '../../../core/models/booking-dto';
import { CustomerSummaryBookingDto, CustomerSummaryDto } from '../../../core/models/customer-summary-dto';

/**
 * The Customer Summary feature's own view model — never used interchangeably with
 * `CustomerSummaryDto` (the frontend model-separation convention, mirrored from
 * `features/customers/models/customer.ts`/`features/bookings/models/booking.ts`).
 */
export interface CustomerSummary {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  isAnonymized: boolean;
  bookings: CustomerSummaryBooking[];
}

/** One booking-history row, with `startDate`/`endDate` parsed into real `Date`s so the
 * page can format them without every consumer re-parsing the wire string itself. */
export interface CustomerSummaryBooking {
  id: string;
  vehicleMake: string;
  vehicleModel: string;
  vehicleRegistrationNumber: string;
  startDate: Date;
  endDate: Date;
  totalPrice: number;
  status: BookingStatusDto;
}

/**
 * Parses a `DateOnly`-shaped wire string (`"yyyy-MM-dd"`, no time/offset component) as
 * LOCAL midnight rather than UTC midnight — mirrors `features/bookings/models/booking.ts`'s
 * exact `parseDateOnly` reasoning, duplicated here per this codebase's "each feature
 * folder stays a self-contained module with no cross-feature imports" convention.
 */
function parseDateOnly(dateOnly: string): Date {
  return new Date(`${dateOnly}T00:00:00`);
}

function toCustomerSummaryBooking(dto: CustomerSummaryBookingDto): CustomerSummaryBooking {
  return {
    id: dto.id,
    vehicleMake: dto.vehicleMake,
    vehicleModel: dto.vehicleModel,
    vehicleRegistrationNumber: dto.vehicleRegistrationNumber,
    startDate: parseDateOnly(dto.startDate),
    endDate: parseDateOnly(dto.endDate),
    totalPrice: dto.totalPrice,
    status: dto.status,
  };
}

/** Explicit DTO -> view-model mapper. Never mapped implicitly / structurally. */
export function toCustomerSummary(dto: CustomerSummaryDto): CustomerSummary {
  return {
    id: dto.id,
    firstName: dto.firstName,
    lastName: dto.lastName,
    email: dto.email,
    phoneNumber: dto.phoneNumber,
    isAnonymized: dto.isAnonymized,
    bookings: dto.bookings.map(toCustomerSummaryBooking),
  };
}
