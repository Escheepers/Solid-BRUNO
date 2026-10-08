import { BookingDto } from '../../../core/models/booking-dto';
import { Booking, displayStatus, isCancellable, isUpcoming, startOfToday, toBooking } from './booking';

describe('toBooking', () => {
  const dto: BookingDto = {
    id: 'b1',
    vehicleId: 'v1',
    vehicleMake: 'Toyota',
    vehicleModel: 'Corolla',
    vehicleRegistrationNumber: 'CA123456',
    customerId: 'c1',
    customerFirstName: 'Thabo',
    customerLastName: 'Nkosi',
    customerIsAnonymized: false,
    startDate: '2026-10-01',
    endDate: '2026-10-05',
    totalPrice: 1400,
    status: 'Active',
    createdDate: '2026-09-20T10:30:00Z',
  };

  it('maps every primitive field across unchanged', () => {
    const booking = toBooking(dto);

    expect(booking.id).toBe(dto.id);
    expect(booking.vehicleId).toBe(dto.vehicleId);
    expect(booking.vehicleMake).toBe(dto.vehicleMake);
    expect(booking.vehicleModel).toBe(dto.vehicleModel);
    expect(booking.vehicleRegistrationNumber).toBe(dto.vehicleRegistrationNumber);
    expect(booking.customerId).toBe(dto.customerId);
    expect(booking.customerFirstName).toBe(dto.customerFirstName);
    expect(booking.customerLastName).toBe(dto.customerLastName);
    expect(booking.customerIsAnonymized).toBe(dto.customerIsAnonymized);
    expect(booking.totalPrice).toBe(dto.totalPrice);
    expect(booking.status).toBe(dto.status);
  });

  it('parses the ISO createdDate string into a real Date', () => {
    const booking = toBooking(dto);

    expect(booking.createdDate).toBeInstanceOf(Date);
    expect(booking.createdDate.toISOString()).toBe(new Date(dto.createdDate).toISOString());
  });

  it('parses startDate/endDate as the same calendar day regardless of local timezone', () => {
    const booking = toBooking(dto);

    expect(booking.startDate).toBeInstanceOf(Date);
    expect(booking.startDate.getFullYear()).toBe(2026);
    expect(booking.startDate.getMonth()).toBe(9); // October, 0-indexed
    expect(booking.startDate.getDate()).toBe(1);

    expect(booking.endDate.getFullYear()).toBe(2026);
    expect(booking.endDate.getMonth()).toBe(9);
    expect(booking.endDate.getDate()).toBe(5);
  });
});

/**
 * Moved here from `bookings-page.spec.ts` (spec-4-5's Scope decision 3 -- the
 * `isCancellable`/`startOfToday` extraction). Originally a pure relocation of the
 * same assertions that exercised this logic while it lived in `bookings-page.ts`;
 * updated by spec-booking-form-error-handling-fixes.md's bugfix from the old,
 * too-lenient `EndDate`-based rule to the corrected `StartDate`-based one -- a
 * booking is only cancellable while it is genuinely still in the future.
 */
describe('isCancellable', () => {
  function booking(overrides: Partial<Booking> = {}): Booking {
    return {
      id: 'b1',
      vehicleId: 'v1',
      vehicleMake: 'Toyota',
      vehicleModel: 'Corolla',
      vehicleRegistrationNumber: 'CA123456',
      customerId: 'c1',
      customerFirstName: 'Thabo',
      customerLastName: 'Nkosi',
      customerIsAnonymized: false,
      startDate: new Date('2099-01-01'),
      endDate: new Date('2099-01-05'),
      totalPrice: 1400,
      status: 'Active',
      createdDate: new Date('2026-09-20T10:30:00Z'),
      ...overrides,
    };
  }

  it('is true for an Active booking with a future StartDate', () => {
    expect(isCancellable(booking({ status: 'Active', startDate: new Date('2099-01-01') }))).toBe(true);
  });

  it('is false for a Completed booking', () => {
    expect(isCancellable(booking({ status: 'Completed' }))).toBe(false);
  });

  it('is false for a Cancelled booking', () => {
    expect(isCancellable(booking({ status: 'Cancelled' }))).toBe(false);
  });

  it('is false for a still-Active booking whose StartDate is already in the past (mid-rental)', () => {
    // The bug this spec fixes: StartDate in the past but EndDate still in the future was
    // previously and incorrectly considered cancellable under the old EndDate-based rule.
    expect(
      isCancellable(
        booking({ status: 'Active', startDate: new Date('2020-01-01'), endDate: new Date('2099-01-05') }),
      ),
    ).toBe(false);
  });

  it('is false for a still-Active booking whose StartDate is exactly today', () => {
    expect(isCancellable(booking({ status: 'Active', startDate: startOfToday() }))).toBe(false);
  });

  it('is false for a still-Active booking whose EndDate is already in the past', () => {
    expect(
      isCancellable(
        booking({ status: 'Active', startDate: new Date('2020-01-01'), endDate: new Date('2020-01-05') }),
      ),
    ).toBe(false);
  });
});

/**
 * "Upcoming" is NOT a stored status (the API only has Active/Completed/Cancelled): it is a display
 * label for an Active booking that has not started yet. A booking started today, mid-rental, or
 * already past its end date stays "Active"; Completed/Cancelled are never relabelled.
 */
describe('isUpcoming / displayStatus', () => {
  const future = new Date('2099-01-01');
  const past = new Date('2020-01-01');

  it('labels an Active booking that has not started yet as Upcoming', () => {
    const booking = { status: 'Active' as const, startDate: future };

    expect(isUpcoming(booking)).toBe(true);
    expect(displayStatus(booking)).toBe('Upcoming');
  });

  it('keeps an Active booking that starts today as Active (it has started)', () => {
    const booking = { status: 'Active' as const, startDate: startOfToday() };

    expect(isUpcoming(booking)).toBe(false);
    expect(displayStatus(booking)).toBe('Active');
  });

  it('keeps a mid-rental or ended-but-unswept Active booking as Active', () => {
    expect(displayStatus({ status: 'Active', startDate: past })).toBe('Active');
  });

  it('never relabels a Completed or Cancelled booking, even with a future start date', () => {
    expect(displayStatus({ status: 'Completed', startDate: future })).toBe('Completed');
    expect(displayStatus({ status: 'Cancelled', startDate: future })).toBe('Cancelled');
  });
});
