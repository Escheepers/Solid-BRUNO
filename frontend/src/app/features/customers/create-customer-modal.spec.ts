import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { CustomerDto } from '../../core/models/customer-dto';
import { ToastService } from '../../shared/toast/toast.service';
import { CreateCustomerModal } from './create-customer-modal';

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

describe('CreateCustomerModal', () => {
  let fixture: ComponentFixture<CreateCustomerModal>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;
  let toastService: ToastService;

  beforeEach(async () => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    await TestBed.configureTestingModule({
      imports: [CreateCustomerModal],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CreateCustomerModal);
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

  it('shows the submit Button as loading while the mutation is pending', async () => {
    await settle();
    fillValidForm();
    await submitForm();

    const submitButton = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(submitButton.disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('.animate-spin')).not.toBeNull();

    httpMock.expectOne('/api/customers').flush(customerDto(), { status: 201, statusText: 'Created' });
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

  it('Escape closes the modal immediately, with no confirmation dialog', async () => {
    await settle();

    const closeSpy = vi.fn();
    fixture.componentInstance.closeRequest.subscribe(closeSpy);

    fillValidForm();

    dialog()!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    fixture.detectChanges();

    expect(closeSpy).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeNull();
  });
});
