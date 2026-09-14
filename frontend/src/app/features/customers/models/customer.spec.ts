import { CustomerDto } from '../../../core/models/customer-dto';
import { toCustomer } from './customer';

describe('toCustomer', () => {
  const dto: CustomerDto = {
    id: 'a1b2c3',
    firstName: 'Jane',
    lastName: 'Doe',
    email: 'jane.doe@example.com',
    phoneNumber: '0821234567',
    createdDate: '2026-01-15T10:30:00Z',
    isDeleted: false,
    isAnonymized: false,
  };

  it('maps every primitive field across unchanged', () => {
    const customer = toCustomer(dto);

    expect(customer.id).toBe(dto.id);
    expect(customer.firstName).toBe(dto.firstName);
    expect(customer.lastName).toBe(dto.lastName);
    expect(customer.email).toBe(dto.email);
    expect(customer.phoneNumber).toBe(dto.phoneNumber);
    expect(customer.isDeleted).toBe(dto.isDeleted);
    expect(customer.isAnonymized).toBe(dto.isAnonymized);
  });

  it('parses the ISO createdDate string into a real Date', () => {
    const customer = toCustomer(dto);

    expect(customer.createdDate).toBeInstanceOf(Date);
    expect(customer.createdDate.toISOString()).toBe(new Date(dto.createdDate).toISOString());
  });
});
