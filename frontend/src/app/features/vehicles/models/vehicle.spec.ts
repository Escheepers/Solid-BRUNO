import { VehicleDto } from '../../../core/models/vehicle-dto';
import { toVehicle } from './vehicle';

describe('toVehicle', () => {
  const dto: VehicleDto = {
    id: 'a1b2c3',
    registrationNumber: 'CA123456',
    make: 'Toyota',
    model: 'Corolla',
    year: 2022,
    dailyRate: 350,
    createdDate: '2026-01-15T10:30:00Z',
  };

  it('maps every primitive field across unchanged', () => {
    const vehicle = toVehicle(dto);

    expect(vehicle.id).toBe(dto.id);
    expect(vehicle.registrationNumber).toBe(dto.registrationNumber);
    expect(vehicle.make).toBe(dto.make);
    expect(vehicle.model).toBe(dto.model);
    expect(vehicle.year).toBe(dto.year);
    expect(vehicle.dailyRate).toBe(dto.dailyRate);
  });

  it('parses the ISO createdDate string into a real Date', () => {
    const vehicle = toVehicle(dto);

    expect(vehicle.createdDate).toBeInstanceOf(Date);
    expect(vehicle.createdDate.toISOString()).toBe(new Date(dto.createdDate).toISOString());
  });
});
