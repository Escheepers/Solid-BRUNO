import { BookingDto, BookingStatusDto } from '../../../core/models/booking-dto';

export type BookingStatus = BookingStatusDto;

/**
 * The Bookings feature's own view model — never used interchangeably with
 * `BookingDto` (the frontend model-separation convention, mirrored from
 * `features/customers/models/customer.ts`/`features/vehicles/models/vehicle.ts`).
 * `startDate`/`endDate`/`createdDate` are parsed into real `Date`s here so the page
 * can format them for display without every consumer re-parsing the wire string
 * itself.
 */
export interface Booking {
  id: string;
  vehicleId: string;
  vehicleMake: string;
  vehicleModel: string;
  vehicleRegistrationNumber: string;
  customerId: string;
  customerFirstName: string;
  customerLastName: string;
  customerIsAnonymized: boolean;
  startDate: Date;
  endDate: Date;
  totalPrice: number;
  status: BookingStatus;
  createdDate: Date;
}

/**
 * Parses a `DateOnly`-shaped wire string (`"yyyy-MM-dd"`, no time/offset component)
 * as LOCAL midnight rather than UTC midnight — `new Date("2026-10-01")` alone would
 * parse as UTC midnight, which then re-renders as the previous calendar day in any
 * timezone behind UTC (a real display bug, not just a style choice). Appending a
 * local-time suffix sidesteps that entirely: the resulting `Date` displays as the
 * same calendar day the backend sent, in every timezone.
 */
function parseDateOnly(dateOnly: string): Date {
  return new Date(`${dateOnly}T00:00:00`);
}

/** Explicit DTO -> view-model mapper. Never mapped implicitly / structurally. */
export function toBooking(dto: BookingDto): Booking {
  return {
    id: dto.id,
    vehicleId: dto.vehicleId,
    vehicleMake: dto.vehicleMake,
    vehicleModel: dto.vehicleModel,
    vehicleRegistrationNumber: dto.vehicleRegistrationNumber,
    customerId: dto.customerId,
    customerFirstName: dto.customerFirstName,
    customerLastName: dto.customerLastName,
    customerIsAnonymized: dto.customerIsAnonymized,
    startDate: parseDateOnly(dto.startDate),
    endDate: parseDateOnly(dto.endDate),
    totalPrice: dto.totalPrice,
    status: dto.status,
    createdDate: new Date(dto.createdDate),
  };
}

/**
 * Local midnight for "today" -- matches `toBooking`'s own `parseDateOnly` convention
 * (local midnight, not UTC midnight) so `booking.startDate > startOfToday()` compares
 * two Dates anchored to the same wall-clock day, never off by a timezone offset.
 *
 * Moved here from `bookings-page.ts` (spec-4-5's Scope decision 3) -- a pure
 * relocation, not a behavior change -- so `BookingDetailPage` can reuse the exact
 * same "today" definition `isCancellable` depends on.
 */
export function startOfToday(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

/**
 * True only for a booking the backend would actually accept a Cancel request for
 * right now (spec-4-3's Boundaries: "never offering an action the backend would
 * always reject") -- Active status and a not-yet-started StartDate (bugfix:
 * spec-booking-form-error-handling-fixes.md corrected this from the backend's own
 * old, too-lenient `EndDate`-based boundary -- a booking is only cancellable while
 * it is genuinely still in the future). Completed, Cancelled, and
 * already-started-still-Active bookings (mid-rental, past-EndDate-unswept, or
 * StartDate exactly today) all get no Cancel action at all; the 409 paths those
 * states would hit are proven at the API/integration level as defensive backstops
 * for a stale UI/race, never exercised by clicking through this app.
 *
 * Moved here from `bookings-page.ts` and exported (spec-4-5's Scope decision 3) so
 * `BookingDetailPage` reuses this exact eligibility check rather than a second copy
 * of the same date comparison (DRY) -- its Cancel action must always agree with the
 * list's.
 */
export function isCancellable(booking: Booking): boolean {
  return booking.status === 'Active' && booking.startDate > startOfToday();
}
