import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { VehicleDto } from '../../core/models/vehicle-dto';
import { ToastService } from '../../shared/toast/toast.service';
import { CreateVehicleModal } from './create-vehicle-modal';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function vehicleDto(overrides: Partial<VehicleDto> = {}): VehicleDto {
  return {
    id: 'v1',
    registrationNumber: 'CA123456',
    make: 'Toyota',
    model: 'Corolla',
    year: 2022,
    dailyRate: 350,
    createdDate: '2026-01-15T10:30:00Z',
    ...overrides,
  };
}

describe('CreateVehicleModal', () => {
  let fixture: ComponentFixture<CreateVehicleModal>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;
  let toastService: ToastService;

  beforeEach(async () => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    await TestBed.configureTestingModule({
      imports: [CreateVehicleModal],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CreateVehicleModal);
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
      registrationNumber: string;
      make: string;
      model: string;
      year: string;
      dailyRate: string;
    }> = {},
  ): void {
    const values = {
      registrationNumber: 'CA999999',
      make: 'Ford',
      model: 'Ranger',
      year: '2023',
      dailyRate: '450',
      ...overrides,
    };
    const [reg, make, model, year, dailyRate] = fields();
    setValue(reg, values.registrationNumber);
    setValue(make, values.make);
    setValue(model, values.model);
    setValue(year, values.year);
    setValue(dailyRate, values.dailyRate);
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

  it('does not call the mutation when required fields are blank', async () => {
    await settle();

    await submitForm();

    httpMock.expectNone('/api/vehicles');
  });

  it('valid submit posts the correct payload; success closes the modal, toasts, and invalidates the list query', async () => {
    await settle();

    const closeSpy = vi.fn();
    fixture.componentInstance.closeRequest.subscribe(closeSpy);
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const toastSpy = vi.spyOn(toastService, 'success');

    fillValidForm();
    await submitForm();

    const req = httpMock.expectOne('/api/vehicles');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      registrationNumber: 'CA999999',
      make: 'Ford',
      model: 'Ranger',
      year: 2023,
      dailyRate: 450,
    });

    req.flush(vehicleDto({ registrationNumber: 'CA999999' }), {
      status: 201,
      statusText: 'Created',
    });
    await settle();

    expect(closeSpy).toHaveBeenCalled();
    expect(toastSpy).toHaveBeenCalledWith('Vehicle created.');
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['vehicles', 'list'] });
  });

  it('shows the submit Button as loading while the mutation is pending', async () => {
    await settle();
    fillValidForm();
    await submitForm();

    const submitButton = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(submitButton.disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('.animate-spin')).not.toBeNull();

    httpMock
      .expectOne('/api/vehicles')
      .flush(vehicleDto(), { status: 201, statusText: 'Created' });
    await settle();
  });

  it('409 duplicate registration shows an inline error on registrationNumber and keeps the modal open', async () => {
    await settle();
    fillValidForm();
    await submitForm();

    const req = httpMock.expectOne('/api/vehicles');
    req.flush(
      {
        type: 'urn:bruno:vehicle:registration-number',
        title: 'A domain rule was violated.',
        status: 409,
        detail: 'This registration number is already in use.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(fixture.nativeElement.textContent).toContain(
      'This registration number is already in use.',
    );
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('400 with errors.Make shows an inline error on the make field, not a banner', async () => {
    await settle();
    fillValidForm();
    await submitForm();

    const req = httpMock.expectOne('/api/vehicles');
    req.flush(
      {
        type: 'https://bruno-vehicle-hire/problems/validation',
        title: 'One or more validation errors occurred.',
        status: 400,
        detail: 'Validation failed.',
        errors: { Make: ['Make must not be empty.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(fixture.nativeElement.textContent).toContain('Make must not be empty.');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('a server error shows a top-of-form banner, not a field-level error', async () => {
    await settle();
    fillValidForm();
    await submitForm();

    const req = httpMock.expectOne('/api/vehicles');
    req.flush('boom', { status: 500, statusText: 'Internal Server Error' });
    await settle();

    const banner = fixture.nativeElement.querySelector('[role="alert"]');
    expect(banner).not.toBeNull();
    expect(banner.textContent).toContain('An unexpected error occurred');

    const submitButton = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(submitButton.disabled).toBe(false);
  });
});
