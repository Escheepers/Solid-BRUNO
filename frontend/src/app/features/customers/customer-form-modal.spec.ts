import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { CustomerDto } from '../../core/models/customer-dto';
import { ToastService } from '../../shared/toast/toast.service';
import { CustomerFormModal } from './customer-form-modal';
import { Customer } from './models/customer';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function customerDto(overrides: Partial<CustomerDto> = {}): CustomerDto {
  return {
    id: 'c1',
    firstName: 'Jane',
    lastName: 'Doe',
    email: 'jane.doe@example.com',
    phoneNumber: '0821234567',
    createdDate: '2026-01-15T10:30:00Z',
    isDeleted: false,
    isAnonymized: false,
    ...overrides,
  };
}

function customer(overrides: Partial<Customer> = {}): Customer {
  return {
    id: 'c1',
    firstName: 'Jane',
    lastName: 'Doe',
    email: 'jane.doe@example.com',
    phoneNumber: '0821234567',
    createdDate: new Date('2026-01-15T10:30:00Z'),
    isDeleted: false,
    isAnonymized: false,
    ...overrides,
  };
}

describe('CustomerFormModal', () => {
  let fixture: ComponentFixture<CustomerFormModal>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;
  let toastService: ToastService;

  beforeEach(async () => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    await TestBed.configureTestingModule({
      imports: [CustomerFormModal],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomerFormModal);
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

  function fields(): HTMLInputElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('app-input input'));
  }

  function setValue(el: HTMLInputElement, value: string): void {
    el.value = value;
    el.dispatchEvent(new Event('input'));
  }

  function fillValidForm(
    overrides: Partial<{
      firstName: string;
      lastName: string;
      email: string;
      phoneNumber: string;
    }> = {},
  ): void {
    const values = {
      firstName: 'Jane',
      lastName: 'Doe',
      email: 'jane.doe@example.com',
      phoneNumber: '0821234567',
      ...overrides,
    };
    const [firstName, lastName, email, phoneNumber] = fields();
    setValue(firstName, values.firstName);
    setValue(lastName, values.lastName);
    setValue(email, values.email);
    setValue(phoneNumber, values.phoneNumber);
    fixture.detectChanges();
  }

  async function submitForm(): Promise<void> {
    // jsdom doesn't reliably fire a native 'submit' event just from clicking a
    // nested `<button type="submit">`, so the form's submit is dispatched directly
    // — Angular's `(ngSubmit)` listens for that DOM event regardless of what
    // triggered it. TanStack Query's `mutate()` dispatches the actual HTTP call
    // asynchronously, so a microtask flush is needed before the request appears.
    const form = fixture.nativeElement.querySelector('form');
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  function dialog(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="dialog"]');
  }

  function alertDialog(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="alertdialog"]');
  }

  function pressEscapeOn(el: HTMLElement): void {
    el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
  }

  describe('create mode (customer = null)', () => {
    it('shows "+ New Customer" as the Modal title', async () => {
      await settle();

      expect(fixture.nativeElement.textContent).toContain('+ New Customer');
    });

    it('does not call the mutation when required fields are blank', async () => {
      await settle();

      await submitForm();

      httpMock.expectNone('/api/customers');
    });

    it('valid submit posts the correct payload; success closes the modal, toasts, and invalidates the list query', async () => {
      await settle();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);
      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      const toastSpy = vi.spyOn(toastService, 'success');

      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/customers');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'jane.doe@example.com',
        phoneNumber: '0821234567',
      });

      req.flush(customerDto(), { status: 201, statusText: 'Created' });
      await settle();

      expect(closeSpy).toHaveBeenCalled();
      expect(toastSpy).toHaveBeenCalledWith('Customer created.');
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['customers', 'list'] });
    });

    it('emits created with the mapped Customer on a successful create', async () => {
      await settle();

      const createdSpy = vi.fn();
      fixture.componentInstance.created.subscribe(createdSpy);

      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/customers');
      req.flush(customerDto({ id: 'c99' }), { status: 201, statusText: 'Created' });
      await settle();

      expect(createdSpy).toHaveBeenCalledWith(customer({ id: 'c99' }));
      expect(createdSpy).toHaveBeenCalledTimes(1);
    });

    it('does not emit created when create fails', async () => {
      await settle();

      const createdSpy = vi.fn();
      fixture.componentInstance.created.subscribe(createdSpy);

      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/customers');
      req.flush('boom', { status: 500, statusText: 'Internal Server Error' });
      await settle();

      expect(createdSpy).not.toHaveBeenCalled();
    });

    it('shows the submit Button as loading while the mutation is pending', async () => {
      await settle();
      fillValidForm();
      await submitForm();

      const submitButton = fixture.nativeElement.querySelector('button[type="submit"]');
      expect(submitButton.disabled).toBe(true);
      expect(fixture.nativeElement.querySelector('.animate-spin')).not.toBeNull();

      httpMock
        .expectOne('/api/customers')
        .flush(customerDto(), { status: 201, statusText: 'Created' });
      await settle();
    });

    it('409 duplicate email shows an inline error on the email field and keeps the modal open', async () => {
      await settle();
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/customers');
      req.flush(
        {
          type: 'urn:bruno:customer:email',
          title: 'A domain rule was violated.',
          status: 409,
          detail: 'This email address is already in use.',
        },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      expect(fixture.nativeElement.textContent).toContain('This email address is already in use.');
      expect(dialog()).not.toBeNull();
      expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    });

    it('400 with errors.FirstName shows an inline error on the firstName field, not a banner', async () => {
      await settle();
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/customers');
      req.flush(
        {
          type: 'https://bruno-vehicle-hire/problems/validation',
          title: 'One or more validation errors occurred.',
          status: 400,
          detail: 'Validation failed.',
          errors: { FirstName: ['First name must not be empty.'] },
        },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();

      expect(fixture.nativeElement.textContent).toContain('First name must not be empty.');
      expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    });

    it('a malformed email shows a client-side validation error without hitting the server', async () => {
      await settle();
      fillValidForm({ email: 'not-an-email' });
      await submitForm();

      httpMock.expectNone('/api/customers');
      expect(fixture.nativeElement.textContent).toContain('Enter a valid email address.');
    });

    it.each([
      ['letters', '08212345a7'],
      ['spaces', '082 123 4567'],
      ['a leading plus', '+27821234567'],
      ['more than 10 digits', '08212345678'],
    ])(
      'a phone number with %s shows a client-side validation error without hitting the server',
      async (_label, phoneNumber) => {
        await settle();
        fillValidForm({ phoneNumber });
        await submitForm();

        httpMock.expectNone('/api/customers');
        expect(fixture.nativeElement.textContent).toContain(
          'Phone number must be digits only, up to 10 digits.',
        );
      },
    );

    it('accepts a phone number of up to 10 digits', async () => {
      await settle();
      fillValidForm({ phoneNumber: '0821234567' });
      await submitForm();

      httpMock.expectOne('/api/customers').flush(customerDto(), { status: 201, statusText: 'Created' });
      expect(fixture.nativeElement.textContent).not.toContain('Phone number must be digits only');
    });

    it('a server error shows a top-of-form banner, not a field-level error', async () => {
      await settle();
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/customers');
      req.flush('boom', { status: 500, statusText: 'Internal Server Error' });
      await settle();

      const banner = fixture.nativeElement.querySelector('[role="alert"]');
      expect(banner).not.toBeNull();
      expect(banner.textContent).toContain('An unexpected error occurred');

      const submitButton = fixture.nativeElement.querySelector('button[type="submit"]');
      expect(submitButton.disabled).toBe(false);
    });
  });

  describe('edit mode (customer set)', () => {
    it('pre-populates every field from the given customer', async () => {
      fixture.componentRef.setInput('customer', customer());
      await settle();

      const [firstName, lastName, email, phoneNumber] = fields();
      expect(firstName.value).toBe('Jane');
      expect(lastName.value).toBe('Doe');
      expect(email.value).toBe('jane.doe@example.com');
      expect(phoneNumber.value).toBe('0821234567');
    });

    it('shows "Edit Customer" as the Modal title', async () => {
      fixture.componentRef.setInput('customer', customer());
      await settle();

      expect(fixture.nativeElement.textContent).toContain('Edit Customer');
    });

    it('a successful edit calls PUT with the right URL/payload, invalidates the list query, shows "Customer updated.", and closes', async () => {
      fixture.componentRef.setInput('customer', customer({ id: 'c42' }));
      await settle();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);
      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      const toastSpy = vi.spyOn(toastService, 'success');

      const [, , , phoneNumber] = fields();
      setValue(phoneNumber, '0839999999');
      fixture.detectChanges();

      await submitForm();

      const req = httpMock.expectOne('/api/customers/c42');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'jane.doe@example.com',
        phoneNumber: '0839999999',
      });

      req.flush(customerDto({ id: 'c42', phoneNumber: '0839999999' }));
      await settle();

      expect(closeSpy).toHaveBeenCalled();
      expect(toastSpy).toHaveBeenCalledWith('Customer updated.');
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['customers', 'list'] });
    });

    it('does not emit created on a successful update', async () => {
      fixture.componentRef.setInput('customer', customer({ id: 'c42' }));
      await settle();

      const createdSpy = vi.fn();
      fixture.componentInstance.created.subscribe(createdSpy);

      const [, , , phoneNumber] = fields();
      setValue(phoneNumber, '0839999999');
      fixture.detectChanges();

      await submitForm();

      const req = httpMock.expectOne('/api/customers/c42');
      req.flush(customerDto({ id: 'c42', phoneNumber: '0839999999' }));
      await settle();

      expect(createdSpy).not.toHaveBeenCalled();
    });

    it('a 409 against a different customer’s email still renders inline on email in edit mode', async () => {
      fixture.componentRef.setInput('customer', customer({ id: 'c42' }));
      await settle();

      const [, , email] = fields();
      setValue(email, 'someone-else@example.com');
      fixture.detectChanges();

      await submitForm();

      const req = httpMock.expectOne('/api/customers/c42');
      req.flush(
        {
          type: 'urn:bruno:customer:email',
          title: 'A domain rule was violated.',
          status: 409,
          detail: 'This email address is already in use.',
        },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      expect(fixture.nativeElement.textContent).toContain('This email address is already in use.');
      expect(dialog()).not.toBeNull();
      expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    });
  });

  /**
   * Covers spec-booking-form-error-handling-fixes.md's reactive-clear-on-change fix:
   * a stale server-side field error must clear the moment the user corrects that
   * exact field, without needing to resubmit -- and must NOT clear when a
   * different field is edited instead.
   */
  describe('reactive field-error clearing', () => {
    async function triggerEmailConflict(): Promise<void> {
      await settle();
      fillValidForm();
      await submitForm();

      const req = httpMock.expectOne('/api/customers');
      req.flush(
        {
          type: 'urn:bruno:customer:email',
          title: 'A domain rule was violated.',
          status: 409,
          detail: 'This email address is already in use.',
        },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      expect(fixture.nativeElement.textContent).toContain('This email address is already in use.');
    }

    it('clears the stale error immediately once the user corrects that same field, before any resubmit', async () => {
      await triggerEmailConflict();

      const [, , email] = fields();
      setValue(email, 'someone-new@example.com');
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).not.toContain(
        'This email address is already in use.',
      );
    });

    it('leaves the error displayed when a different field is edited instead', async () => {
      await triggerEmailConflict();

      const [firstName] = fields();
      setValue(firstName, 'Zola');
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('This email address is already in use.');
    });
  });

  describe('discard guard', () => {
    it('an untouched form closes immediately on Escape, with no ConfirmDialog', async () => {
      await settle();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);

      pressEscapeOn(dialog()!);
      fixture.detectChanges();

      expect(closeSpy).toHaveBeenCalled();
      expect(alertDialog()).toBeNull();
    });

    it('a touched form shows the ConfirmDialog on Escape and keeps the Modal open', async () => {
      await settle();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);

      const [firstName] = fields();
      setValue(firstName, 'Zola');
      fixture.detectChanges();

      pressEscapeOn(dialog()!);
      fixture.detectChanges();

      expect(closeSpy).not.toHaveBeenCalled();
      expect(dialog()).not.toBeNull();
      expect(alertDialog()).not.toBeNull();
      expect(alertDialog()!.textContent).toContain('Discard changes?');
    });

    it('clicking Discard closes and resets the form', async () => {
      await settle();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);

      const [firstName] = fields();
      setValue(firstName, 'Zola');
      fixture.detectChanges();
      pressEscapeOn(dialog()!);
      fixture.detectChanges();

      const discardButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Discard');
      discardButton!.click();
      fixture.detectChanges();

      expect(closeSpy).toHaveBeenCalled();
      expect(alertDialog()).toBeNull();
      expect(fields()[0].value).toBe('');
    });

    it('clicking Keep editing closes the ConfirmDialog, keeps the Modal open, and preserves the typed value', async () => {
      await settle();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);

      const [firstName] = fields();
      setValue(firstName, 'Zola');
      fixture.detectChanges();
      pressEscapeOn(dialog()!);
      fixture.detectChanges();

      const keepEditingButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Keep editing');
      keepEditingButton!.click();
      fixture.detectChanges();

      expect(closeSpy).not.toHaveBeenCalled();
      expect(alertDialog()).toBeNull();
      expect(dialog()).not.toBeNull();
      expect(fields()[0].value).toBe('Zola');
    });
  });
});
