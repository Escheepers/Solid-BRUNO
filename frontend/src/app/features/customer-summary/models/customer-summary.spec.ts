import { CustomerSummaryDto } from '../../../core/models/customer-summary-dto';
import { toCustomerSummary } from './customer-summary';

describe('toCustomerSummary', () => {
  const dto: CustomerSummaryDto = {
    id: 'c1',
    firstName: 'Thabo',
    lastName: 'Nkosi',
    email: 'thabo@example.com',
    phoneNumber: '0821234567',
    isAnonymized: false,
    bookings: [
      {
        id: 'b1',
        vehicleMake: 'Toyota',
        vehicleModel: 'Corolla',
        vehicleRegistrationNumber: 'CA123456',
        startDate: '2026-10-01',
        endDate: '2026-10-05',
        totalPrice: 1400,
        status: 'Active',
      },
    ],
  };

  it('maps every primitive field across unchanged', () => {
    const summary = toCustomerSummary(dto);

    expect(summary.id).toBe(dto.id);
    expect(summary.firstName).toBe(dto.firstName);
    expect(summary.lastName).toBe(dto.lastName);
    expect(summary.email).toBe(dto.email);
    expect(summary.phoneNumber).toBe(dto.phoneNumber);
    expect(summary.isAnonymized).toBe(dto.isAnonymized);
  });

  it('maps each booking, parsing startDate/endDate as local-midnight Dates', () => {
    const summary = toCustomerSummary(dto);

    expect(summary.bookings.length).toBe(1);
    const booking = summary.bookings[0];
    const dtoBooking = dto.bookings[0];

    expect(booking.id).toBe(dtoBooking.id);
    expect(booking.vehicleMake).toBe(dtoBooking.vehicleMake);
    expect(booking.vehicleModel).toBe(dtoBooking.vehicleModel);
    expect(booking.vehicleRegistrationNumber).toBe(dtoBooking.vehicleRegistrationNumber);
    expect(booking.totalPrice).toBe(dtoBooking.totalPrice);
    expect(booking.status).toBe(dtoBooking.status);

    expect(booking.startDate).toBeInstanceOf(Date);
    expect(booking.startDate.toISOString()).toBe(new Date('2026-10-01T00:00:00').toISOString());
    expect(booking.endDate.toISOString()).toBe(new Date('2026-10-05T00:00:00').toISOString());
  });

  it('maps a customer with zero bookings to an empty bookings array', () => {
    const summary = toCustomerSummary({ ...dto, bookings: [] });

    expect(summary.bookings).toEqual([]);
  });
});
