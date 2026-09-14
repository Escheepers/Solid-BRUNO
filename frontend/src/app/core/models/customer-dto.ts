/**
 * Raw wire-format DTO, matching `CustomerDto` in
 * `src/BrunoVehicleHire.Application/Customers/Dtos/CustomerDto.cs` exactly (camelCase,
 * `createdDate` as an ISO date string). Never used directly by feature components —
 * `features/customers/models/customer.ts`'s `toCustomer` mapper converts it to the
 * feature's own `Customer` view model first, per the frontend model-separation
 * convention (core DTOs vs. feature view models are never used interchangeably).
 */
export interface CustomerDto {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  createdDate: string;
  isDeleted: boolean;
  isAnonymized: boolean;
}
