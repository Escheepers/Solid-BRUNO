import { BookingDto } from '../../../core/models/booking-dto';
import { toBooking } from './booking';

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
