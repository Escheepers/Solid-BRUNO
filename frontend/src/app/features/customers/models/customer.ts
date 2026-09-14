import { CustomerDto } from '../../../core/models/customer-dto';

/**
 * The Customers feature's own view model — never used interchangeably with
 * `CustomerDto` (the frontend model-separation convention, mirrored from
 * `features/vehicles/models/vehicle.ts`, spec-1-7). `createdDate` is parsed into a
 * real `Date` here (rather than kept as the DTO's raw ISO string) so the page can
 * format it for display without every consumer re-parsing the wire string itself.
 */
export interface Customer {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  createdDate: Date;
  isDeleted: boolean;
  isAnonymized: boolean;
}

/** Explicit DTO -> view-model mapper. Never mapped implicitly / structurally. */
export function toCustomer(dto: CustomerDto): Customer {
  return {
    id: dto.id,
    firstName: dto.firstName,
    lastName: dto.lastName,
    email: dto.email,
    phoneNumber: dto.phoneNumber,
    createdDate: new Date(dto.createdDate),
    isDeleted: dto.isDeleted,
    isAnonymized: dto.isAnonymized,
  };
}
