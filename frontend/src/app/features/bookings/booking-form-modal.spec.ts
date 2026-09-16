import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { BookingDto } from '../../core/models/booking-dto';
import { CustomerDto } from '../../core/models/customer-dto';
import { VehicleDto } from '../../core/models/vehicle-dto';
import { ToastService } from '../../shared/toast/toast.service';
import { BookingFormModal } from './booking-form-modal';
import { currencyFormatter } from './booking-formatters';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function vehicleDto(overrides: Partial<VehicleDto> = {}): VehicleDto {
  return {
    id: 'v1',
    registrationNumber: 'CA123456',
    make: 'Toyota',
    model: 'Corolla',
    year: 2023,
    dailyRate: 350,
    createdDate: '2026-01-01T00:00:00Z',
    isDeleted: false,
    ...overrides,
  };
}

function customerDto(overrides: Partial<CustomerDto> = {}): CustomerDto {
  return {
    id: 'c1',
    firstName: 'Thabo',
    lastName: 'Nkosi',
    email: 'thabo@example.com',
    phoneNumber: '0821234567',
    createdDate: '2026-01-01T00:00:00Z',
    isDeleted: false,
    isAnonymized: false,
    ...overrides,
  };
}

function bookingDto(overrides: Partial<BookingDto> = {}): BookingDto {
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
    startDate: '2026-10-01',
    endDate: '2026-10-05',
    totalPrice: 1400,
    status: 'Active',
    createdDate: '2026-09-20T10:30:00Z',
    ...overrides,
  };
}

describe('BookingFormModal', () => {
  let fixture: ComponentFixture<BookingFormModal>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;
  let toastService: ToastService;

  beforeEach(async () => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    await TestBed.configureTestingModule({
      imports: [BookingFormModal],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BookingFormModal);
    fixture.componentRef.setInput('open', true);
    httpMock = TestBed.inject(HttpTestingController);
    toastService = TestBed.inject(ToastService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
  }

  /** Flushes the vehicle/customer picker queries that fire as soon as this
   * always-mounted component initializes, regardless of `open`. */
  async function seedPickers(
    vehicles: VehicleDto[] = [vehicleDto()],
    customers: CustomerDto[] = [customerDto()],
  ): Promise<void> {
    await settle();

    const vehiclesReq = httpMock.expectOne(
      (r) => r.url === '/api/vehicles' && r.params.get('pageSize') === '100',
    );
    vehiclesReq.flush({ items: vehicles, totalCount: vehicles.length, page: 1, pageSize: 100 });

    const customersReq = httpMock.expectOne(
      (r) => r.url === '/api/customers' && r.params.get('pageSize') === '100',
    );
    customersReq.flush({ items: customers, totalCount: customers.length, page: 1, pageSize: 100 });

    await settle();
  }

  function dialogs(): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[role="dialog"]'));
  }

  function alertDialog(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="alertdialog"]');
  }

  function vehicleSelect(): HTMLInputElement {
    return fixture.nativeElement.querySelector('#booking-vehicle');
  }

  function customerSelect(): HTMLInputElement {
    return fixture.nativeElement.querySelector('#booking-customer');
  }

  function dateInputs(): HTMLInputElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('app-input input'));
  }

  function setInputValue(el: HTMLInputElement, value: string): void {
    el.value = value;
    el.dispatchEvent(new Event('input'));
  }

  /**
   * Drives the `Combobox` the same way a real keyboard-or-mouse user would --
   * opens the popup (ArrowDown), then clicks the option carrying the given
   * `optionValue()` (its `data-value`, e.g. an id) -- replacing the old
   * `select.value = ...; dispatchEvent(new Event('change'))` shortcut a plain
   * `<select>` allowed.
   */
  function selectComboboxOption(comboboxInput: HTMLInputElement, value: string): void {
    comboboxInput.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
    );
    fixture.detectChanges();

    const option = comboboxInput
      .closest('div')!
      .querySelector(`[role="listbox"] [data-value="${value}"]`) as HTMLElement;

    option.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    option.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  function totalPriceRegion(): HTMLElement {
    return fixture.nativeElement.querySelector('[data-testid="total-price"]');
  }

  function fillValidForm(): void {
    selectComboboxOption(vehicleSelect(), 'v1');
    selectComboboxOption(customerSelect(), 'c1');
    const [startDate, endDate] = dateInputs();
    setInputValue(startDate, '2026-10-01');
    setInputValue(endDate, '2026-10-05');
    fixture.detectChanges();
  }

  async function submitForm(): Promise<void> {
    const form = fixture.nativeElement.querySelector('form');
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function newCustomerButton(): HTMLButtonElement {
    return Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === '+ New Customer',
    )!;
  }

  it('renders vehicle/customer options fetched from the existing paginated queries', async () => {
    await seedPickers(
      [vehicleDto({ id: 'v1', make: 'Toyota', model: 'Corolla', registrationNumber: 'CA123456' })],
      [customerDto({ id: 'c1', firstName: 'Thabo', lastName: 'Nkosi' })],
    );

    vehicleSelect().dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
    );
    fixture.detectChanges();
    const vehicleOptions = Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('#booking-vehicle ~ [role="listbox"] [role="option"]'),
    );
    expect(vehicleOptions.some((o) => o.textContent?.includes('Toyota Corolla — CA123456'))).toBe(
      true,
    );

    customerSelect().dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
    );
    fixture.detectChanges();
    const customerOptions = Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('#booking-customer ~ [role="listbox"] [role="option"]'),
    );
    expect(customerOptions.some((o) => o.textContent?.includes('Thabo Nkosi'))).toBe(true);
  });

  it('does not call the create mutation when required fields are blank', async () => {
    await seedPickers();

    await submitForm();

    httpMock.expectNone('/api/bookings');
    expect(fixture.nativeElement.textContent).toContain('This field is required.');
  });

  it('valid submit posts the correct payload; success toasts, closes, and invalidates the list query', async () => {
    await seedPickers();

    const closeSpy = vi.fn();
    fixture.componentInstance.closeRequest.subscribe(closeSpy);
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const toastSpy = vi.spyOn(toastService, 'success');

    fillValidForm();
    await submitForm();

    const req = httpMock.expectOne('/api/bookings');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      vehicleId: 'v1',
      customerId: 'c1',
      startDate: '2026-10-01',
      endDate: '2026-10-05',
    });

    req.flush(bookingDto(), { status: 201, statusText: 'Created' });
    await settle();

    expect(closeSpy).toHaveBeenCalled();
    expect(toastSpy).toHaveBeenCalledWith('Booking created.');
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['bookings', 'list'] });
  });

  describe('live Total Price', () => {
    it('shows a placeholder until a vehicle and valid dates are all selected', async () => {
      await seedPickers();

      expect(totalPriceRegion().textContent).toContain('Select a vehicle and valid dates');
    });

    it('computes and updates Total Price live as the vehicle and dates change, in an aria-live="polite" region', async () => {
      await seedPickers([vehicleDto({ id: 'v1', dailyRate: 350 })]);

      expect(totalPriceRegion().getAttribute('aria-live')).toBe('polite');

      selectComboboxOption(vehicleSelect(), 'v1');
      const [startDate, endDate] = dateInputs();
      setInputValue(startDate, '2026-10-01');
      setInputValue(endDate, '2026-10-05');
      fixture.detectChanges();

      // 4 days x R350 = R1,400.00
      expect(totalPriceRegion().textContent).toContain(currencyFormatter.format(1400));

      setInputValue(endDate, '2026-10-03');
      fixture.detectChanges();

      // 2 days x R350 = R700.00
      expect(totalPriceRegion().textContent).toContain(currencyFormatter.format(700));
      expect(totalPriceRegion().textContent).not.toContain(currencyFormatter.format(1400));
    });

    it('shows the placeholder again when endDate is not after startDate', async () => {
      await seedPickers([vehicleDto({ id: 'v1', dailyRate: 350 })]);

      selectComboboxOption(vehicleSelect(), 'v1');
      const [startDate, endDate] = dateInputs();
      setInputValue(startDate, '2026-10-05');
      setInputValue(endDate, '2026-10-05');
      fixture.detectChanges();

      expect(totalPriceRegion().textContent).toContain('Select a vehicle and valid dates');
    });
  });

  describe('server error mapping', () => {
    it('a 400 EndDate error renders inline under the End Date field', async () => {
      await seedPickers();
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/bookings');
      req.flush(
        {
          type: 'https://bruno-vehicle-hire/problems/validation',
          title: 'One or more validation errors occurred.',
          status: 400,
          detail: 'Validation failed.',
          errors: { EndDate: ['EndDate must be after StartDate.'] },
        },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();

      expect(fixture.nativeElement.textContent).toContain('EndDate must be after StartDate.');
      expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    });

    it('a 409 soft-deleted-vehicle error renders inline under the Vehicle field with the exact detail message', async () => {
      await seedPickers();
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/bookings');
      req.flush(
        {
          type: 'urn:bruno:vehicle:is-deleted',
          title: 'A domain rule was violated.',
          status: 409,
          detail: 'This vehicle is not available.',
        },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      expect(fixture.nativeElement.textContent).toContain('This vehicle is not available.');
      expect(dialogs().length).toBe(1);
      expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    });

    it('a 409 overlap error renders inline under the End Date field, not Vehicle', async () => {
      await seedPickers();
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/bookings');
      req.flush(
        {
          type: 'urn:bruno:booking:overlap',
          title: 'A domain rule was violated.',
          status: 409,
          detail: 'This vehicle is already booked 1 Oct – 5 Oct',
        },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      expect(fixture.nativeElement.textContent).toContain('This vehicle is already booked 1 Oct – 5 Oct');
      expect(dialogs().length).toBe(1);
      expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();

      // The message renders next to End Date, not Vehicle -- proving it was mapped
      // onto the endDate control rather than hardcoded onto vehicleId (the bug this
      // spec fixes).
      const endDateInput = dateInputs()[1];
      const endDateContainer = endDateInput.closest('app-input');
      expect(endDateContainer?.textContent).toContain('This vehicle is already booked 1 Oct – 5 Oct');

      const vehicleErrorRegion = fixture.nativeElement.querySelector('#booking-vehicle-error');
      expect(vehicleErrorRegion?.textContent?.trim()).toBe('');
    });

    it('a 404 nonexistent/inactive customer falls through to a top-of-form banner, not a field error', async () => {
      await seedPickers();
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/bookings');
      req.flush(
        {
          type: 'https://bruno-vehicle-hire/problems/not-found',
          title: 'The requested resource was not found.',
          status: 404,
          detail: 'Customer was not found.',
        },
        { status: 404, statusText: 'Not Found' },
      );
      await settle();

      const banner = fixture.nativeElement.querySelector('[role="alert"]');
      expect(banner).not.toBeNull();
      expect(banner.textContent).toContain('Customer was not found.');
    });

    it('a server error shows a top-of-form banner', async () => {
      await seedPickers();
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/bookings');
      req.flush('boom', { status: 500, statusText: 'Internal Server Error' });
      await settle();

      const banner = fixture.nativeElement.querySelector('[role="alert"]');
      expect(banner).not.toBeNull();
      expect(banner.textContent).toContain('An unexpected error occurred');
    });
  });

  /**
   * Covers spec-booking-form-error-handling-fixes.md's reactive-clear-on-change fix:
   * a stale server-side field error must clear the moment the user corrects that
   * exact field, without needing to resubmit -- and must NOT clear when a
   * different field is edited instead.
   */
  describe('reactive field-error clearing', () => {
    async function triggerSoftDeletedVehicleConflict(): Promise<void> {
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/bookings');
      req.flush(
        {
          type: 'urn:bruno:vehicle:is-deleted',
          title: 'A domain rule was violated.',
          status: 409,
          detail: 'This vehicle is not available.',
        },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      expect(fixture.nativeElement.textContent).toContain('This vehicle is not available.');
    }

    it('clears the stale error immediately once the user corrects that same field, before any resubmit', async () => {
      await seedPickers([vehicleDto({ id: 'v1' }), vehicleDto({ id: 'v2', make: 'Ford', model: 'Ranger' })]);
      await triggerSoftDeletedVehicleConflict();

      selectComboboxOption(vehicleSelect(), 'v2');
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).not.toContain('This vehicle is not available.');
    });

    it('leaves the error displayed when a different field is edited instead', async () => {
      await seedPickers();
      await triggerSoftDeletedVehicleConflict();

      const [startDate] = dateInputs();
      setInputValue(startDate, '2026-10-02');
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('This vehicle is not available.');
    });
  });

  describe('nested "+ New Customer" modal', () => {
    it('opens CustomerFormModal on top of the still-open Booking modal', async () => {
      await seedPickers();
      fillValidForm();

      expect(dialogs().length).toBe(1);

      newCustomerButton().click();
      fixture.detectChanges();

      expect(dialogs().length).toBe(2);
      expect(fixture.nativeElement.textContent).toContain('+ New Customer');
    });

    it('creating a customer succeeds without losing the vehicle/dates already entered, and auto-selects the new customer', async () => {
      await seedPickers();
      fillValidForm();

      newCustomerButton().click();
      fixture.detectChanges();

      const nestedFields = Array.from<HTMLInputElement>(
        fixture.nativeElement.querySelectorAll('app-customer-form-modal app-input input'),
      );
      setInputValue(nestedFields[0], 'New');
      setInputValue(nestedFields[1], 'Guy');
      setInputValue(nestedFields[2], 'new.guy@example.com');
      setInputValue(nestedFields[3], '0820000000');
      fixture.detectChanges();

      const nestedForm = fixture.nativeElement.querySelector('app-customer-form-modal form');
      nestedForm.dispatchEvent(new Event('submit', { cancelable: true }));
      await settle();

      const createReq = httpMock.expectOne('/api/customers');
      createReq.flush(customerDto({ id: 'new-c', firstName: 'New', lastName: 'Guy' }), {
        status: 201,
        statusText: 'Created',
      });
      await settle();

      // useCreateCustomerMutation's own onSuccess invalidates ['customers', 'list'],
      // which this form's own picker query shares a prefix with -- it refetches.
      const refetchReq = httpMock.expectOne(
        (r) => r.url === '/api/customers' && r.params.get('pageSize') === '100',
      );
      refetchReq.flush(
        {
          items: [customerDto(), customerDto({ id: 'new-c', firstName: 'New', lastName: 'Guy' })],
          totalCount: 2,
          page: 1,
          pageSize: 100,
        },
        { status: 200, statusText: 'OK' },
      );
      await settle();

      expect(dialogs().length).toBe(1);
      // The combobox resolves the newly-selected customer's display label
      // correctly (also exercises the "value set before its label is
      // resolvable" case, since the refetch above lands after the `setValue`).
      expect(customerSelect().value).toBe('New Guy');
      // Vehicle/dates entered before opening the nested modal are untouched.
      expect(vehicleSelect().value).toBe('Toyota Corolla — CA123456');
      expect(dateInputs()[0].value).toBe('2026-10-01');
      expect(dateInputs()[1].value).toBe('2026-10-05');
      // spec-6-4: focus explicitly returns to the Customer select on this
      // path, not wherever FocusTrap's default "return to trigger" (the
      // "+ New Customer" button) would otherwise land it.
      expect(document.activeElement).toBe(customerSelect());
    });

    it('returns focus to the Customer select when the nested modal is cancelled instead of completed', async () => {
      await seedPickers();
      fillValidForm();

      newCustomerButton().focus();
      newCustomerButton().click();
      fixture.detectChanges();
      expect(dialogs().length).toBe(2);

      const nestedDialog = dialogs()[1];
      nestedDialog.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
      );
      await settle();

      expect(dialogs().length).toBe(1);
      // spec-6-4: cancelling the nested modal returns focus to the Customer
      // select too -- the same carve-out applies on both the "created" and
      // "cancelled" paths, per EXPERIENCE.md.
      expect(document.activeElement).toBe(customerSelect());
    });
  });

  describe('discard guard', () => {
    it('an untouched form closes immediately on Escape, with no ConfirmDialog', async () => {
      await seedPickers();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);

      dialogs()[0].dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
      );
      fixture.detectChanges();

      expect(closeSpy).toHaveBeenCalled();
      expect(alertDialog()).toBeNull();
    });

    it('a touched form shows the ConfirmDialog on Escape and keeps the Modal open', async () => {
      await seedPickers();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);

      selectComboboxOption(vehicleSelect(), 'v1');
      fixture.detectChanges();

      dialogs()[0].dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
      );
      fixture.detectChanges();

      expect(closeSpy).not.toHaveBeenCalled();
      expect(alertDialog()).not.toBeNull();
      expect(alertDialog()!.textContent).toContain('Discard changes?');
    });
  });
});
