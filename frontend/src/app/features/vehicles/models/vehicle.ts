import { VehicleDto } from '../../../core/models/vehicle-dto';

/**
 * The Vehicles feature's own view model — never used interchangeably with
 * `VehicleDto` (the frontend model-separation convention's first real exercise, per
 * spec-1-7). `createdDate` is parsed into a real `Date` here (rather than kept as the
 * DTO's raw ISO string) so the page can format it for display without every
 * consumer re-parsing the wire string itself.
 */
export interface Vehicle {
  id: string;
  registrationNumber: string;
  make: string;
  model: string;
  year: number;
  dailyRate: number;
  createdDate: Date;
  isDeleted: boolean;
}

/** Explicit DTO -> view-model mapper. Never mapped implicitly / structurally. */
export function toVehicle(dto: VehicleDto): Vehicle {
  return {
    id: dto.id,
    registrationNumber: dto.registrationNumber,
    make: dto.make,
    model: dto.model,
    year: dto.year,
    dailyRate: dto.dailyRate,
    createdDate: new Date(dto.createdDate),
    isDeleted: dto.isDeleted,
  };
}
