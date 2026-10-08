import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { VehicleDto } from '../../core/models/vehicle-dto';
import { ToastService } from '../../shared/toast/toast.service';
import { Vehicle } from './models/vehicle';
import { VehicleFormModal } from './vehicle-form-modal';

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
    isDeleted: false,
    ...overrides,
  };
}

function vehicle(overrides: Partial<Vehicle> = {}): Vehicle {
  return {
    id: 'v1',
    registrationNumber: 'CA123456',
    make: 'Toyota',
    model: 'Corolla',
    year: 2022,
    dailyRate: 350,
    createdDate: new Date('2026-01-15T10:30:00Z'),
    isDeleted: false,
    ...overrides,
  };
}

describe('VehicleFormModal', () => {
  let fixture: ComponentFixture<VehicleFormModal>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;
  let toastService: ToastService;

  beforeEach(async () => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    await TestBed.configureTestingModule({
      imports: [VehicleFormModal],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VehicleFormModal);
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

  function dialog(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="dialog"]');
  }

  function alertDialog(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="alertdialog"]');
  }

  function pressEscapeOn(el: HTMLElement): void {
    el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
  }

  describe('create mode (vehicle = null)', () => {
    it('does not call the mutation when required fields are blank', async () => {
      await settle();

      await submitForm();

      httpMock.expectNone('/api/vehicles');
    });

    it('limits the Year field to 1900 through next calendar year', async () => {
      await settle();

      const [, , , year] = fields();
      expect(year.min).toBe('1900');
      expect(year.max).toBe(String(new Date().getFullYear() + 1));
    });

    it.each(['1899', '20233'])('blocks submit and shows a range error for the out-of-range year %s', async (badYear) => {
      await settle();
      fillValidForm({ year: badYear });
      await submitForm();

      httpMock.expectNone('/api/vehicles');
      expect(fixture.nativeElement.textContent).toContain('Year must be between 1900 and');
    });

    it('valid submit posts the correct payload; success closes the modal, toasts, and invalidates the list query', async () => {
      await settle();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);
      const createdSpy = vi.fn();
      fixture.componentInstance.created.subscribe(createdSpy);
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
      expect(createdSpy).toHaveBeenCalledTimes(1);
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
      expect(dialog()).not.toBeNull();
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

  describe('edit mode (vehicle set)', () => {
    it('pre-populates every field from the given vehicle', async () => {
      fixture.componentRef.setInput('vehicle', vehicle());
      await settle();

      const [reg, make, model, year, dailyRate] = fields();
      expect(reg.value).toBe('CA123456');
      expect(make.value).toBe('Toyota');
      expect(model.value).toBe('Corolla');
      expect(year.value).toBe('2022');
      expect(dailyRate.value).toBe('350');
    });

    it('shows "Edit Vehicle" as the Modal title', async () => {
      fixture.componentRef.setInput('vehicle', vehicle());
      await settle();

      expect(fixture.nativeElement.textContent).toContain('Edit Vehicle');
    });

    it('a successful edit calls PUT with the right URL/payload, invalidates the list query, shows "Vehicle updated.", and closes', async () => {
      fixture.componentRef.setInput('vehicle', vehicle({ id: 'v42' }));
      await settle();

      const closeSpy = vi.fn();
      fixture.componentInstance.closeRequest.subscribe(closeSpy);
      const createdSpy = vi.fn();
      fixture.componentInstance.created.subscribe(createdSpy);
      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      const toastSpy = vi.spyOn(toastService, 'success');

      const [, , , , dailyRate] = fields();
      setValue(dailyRate, '500');
      fixture.detectChanges();

      await submitForm();

      const req = httpMock.expectOne('/api/vehicles/v42');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({
        registrationNumber: 'CA123456',
        make: 'Toyota',
        model: 'Corolla',
        year: 2022,
        dailyRate: 500,
      });

      req.flush(vehicleDto({ id: 'v42', dailyRate: 500 }));
      await settle();

      expect(closeSpy).toHaveBeenCalled();
      expect(createdSpy).not.toHaveBeenCalled(); // an edit is not a create
      expect(toastSpy).toHaveBeenCalledWith('Vehicle updated.');
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['vehicles', 'list'] });
    });

    it('a 409 against a different vehicle’s registration number still renders inline on registrationNumber in edit mode', async () => {
      fixture.componentRef.setInput('vehicle', vehicle({ id: 'v42' }));
      await settle();

      const [reg] = fields();
      setValue(reg, 'CA000001');
      fixture.detectChanges();

      await submitForm();

      const req = httpMock.expectOne('/api/vehicles/v42');
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
    async function triggerRegistrationNumberConflict(): Promise<void> {
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
    }

    it('clears the stale error immediately once the user corrects that same field, before any resubmit', async () => {
      await triggerRegistrationNumberConflict();

      const [reg] = fields();
      setValue(reg, 'CA111111');
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).not.toContain(
        'This registration number is already in use.',
      );
    });

    it('leaves the error displayed when a different field is edited instead', async () => {
      await triggerRegistrationNumberConflict();

      const [, make] = fields();
      setValue(make, 'Volkswagen');
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain(
        'This registration number is already in use.',
      );
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

      const [reg] = fields();
      setValue(reg, 'CA555555');
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

      const [reg] = fields();
      setValue(reg, 'CA555555');
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

      const [reg] = fields();
      setValue(reg, 'CA555555');
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
      expect(fields()[0].value).toBe('CA555555');
    });
  });
});
