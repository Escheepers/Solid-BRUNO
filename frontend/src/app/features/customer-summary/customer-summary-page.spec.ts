import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';
import { BehaviorSubject } from 'rxjs';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { CustomerSummaryBookingDto, CustomerSummaryDto } from '../../core/models/customer-summary-dto';
import { currencyFormatter, dateFormatter } from './customer-summary-formatters';
import { CustomerSummaryPage } from './customer-summary-page';

function customerSummaryBookingDto(
  overrides: Partial<CustomerSummaryBookingDto> = {},
): CustomerSummaryBookingDto {
  return {
    id: 'b1',
    vehicleMake: 'Toyota',
    vehicleModel: 'Corolla',
    vehicleRegistrationNumber: 'CA123456',
    startDate: '2026-10-01',
    endDate: '2026-10-05',
    totalPrice: 1400,
    status: 'Active',
    ...overrides,
  };
}

function customerSummaryDto(overrides: Partial<CustomerSummaryDto> = {}): CustomerSummaryDto {
  return {
    id: 'c1',
    firstName: 'Thabo',
    lastName: 'Nkosi',
    email: 'thabo@example.com',
    phoneNumber: '0821234567',
    isAnonymized: false,
    bookings: [],
    ...overrides,
  };
}

/** Matches the macrotask-flush pattern used elsewhere for TanStack Query's Angular reactivity. */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('CustomerSummaryPage', () => {
  let fixture: ComponentFixture<CustomerSummaryPage>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;

  function createComponent(id: string): void {
    const paramMap$ = new BehaviorSubject(convertToParamMap({ id }));

    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    TestBed.configureTestingModule({
      imports: [CustomerSummaryPage],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$ } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomerSummaryPage);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
  }

  it('renders Skeleton in a card container while the request is in flight, not a table', async () => {
    createComponent('c1');
    await settle();

    expect(fixture.nativeElement.querySelector('app-skeleton')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('table')).toBeNull();

    httpMock.expectOne('/api/customers/c1/summary').flush(customerSummaryDto());
  });

  it('renders "This customer no longer exists" and a working link back to the list on a 404', async () => {
    createComponent('missing-id');
    await settle();

    const req = httpMock.expectOne('/api/customers/missing-id/summary');
    req.flush(
      {
        type: 'https://bruno-vehicle-hire/problems/not-found',
        title: 'Not Found',
        status: 404,
        detail: "Customer 'missing-id' was not found.",
      },
      { status: 404, statusText: 'Not Found' },
    );
    await settle();

    expect(fixture.nativeElement.textContent).toContain('This customer no longer exists');
    expect(fixture.nativeElement.querySelector('app-skeleton')).toBeNull();

    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('a');
    expect(link).not.toBeNull();
    expect(link.getAttribute('href')).toBe('/customers');
  });

  it('renders a generic fallback message for a non-not-found error, never a blank page', async () => {
    createComponent('c3');
    await settle();

    httpMock.expectOne('/api/customers/c3/summary').flush('boom', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    await settle();

    expect(fixture.nativeElement.textContent?.trim().length).toBeGreaterThan(0);
    expect(fixture.nativeElement.textContent).not.toContain('This customer no longer exists');
  });

  it('renders identity + contact and no navigation/edit chrome for a customer with booking history', async () => {
    createComponent('c1');
    await settle();

    httpMock.expectOne('/api/customers/c1/summary').flush(
      customerSummaryDto({
        firstName: 'Thabo',
        lastName: 'Nkosi',
        email: 'thabo@example.com',
        phoneNumber: '0821234567',
        bookings: [customerSummaryBookingDto(), customerSummaryBookingDto({ id: 'b2', status: 'Completed' })],
      }),
    );
    await settle();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Thabo Nkosi');
    expect(text).toContain('thabo@example.com');
    expect(text).toContain('0821234567');
    expect(text).toContain('Toyota Corolla — CA123456');
    expect(text).toContain(currencyFormatter.format(1400));
    expect(text).toContain(dateFormatter.format(new Date('2026-10-01T00:00:00')));

    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);

    const badges = fixture.nativeElement.querySelectorAll('tbody app-badge');
    expect(badges.length).toBe(2);
    expect(badges[0].textContent).toContain('Active');
    expect(badges[1].textContent).toContain('Completed');

    // No pagination chrome (this page's table is a plain <table>, not DataTable) and no
    // edit/mutating buttons anywhere on the page (Scope decisions 4 and 6).
    expect(fixture.nativeElement.textContent).not.toContain('Showing');
    expect(fixture.nativeElement.textContent).not.toContain('Previous');
    expect(fixture.nativeElement.textContent).not.toContain('Next');
    const buttons: HTMLButtonElement[] = Array.from(fixture.nativeElement.querySelectorAll('button'));
    expect(buttons.map((b) => b.textContent?.trim())).toEqual(['Print']);
  });

  it('shows "No bookings on record for this customer" in place of the list when there are none', async () => {
    createComponent('c1');
    await settle();

    httpMock.expectOne('/api/customers/c1/summary').flush(customerSummaryDto({ bookings: [] }));
    await settle();

    expect(fixture.nativeElement.textContent).toContain('No bookings on record for this customer');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
  });

  it('renders "Customer (anonymized)" in muted-italic styling, with Email/PhoneNumber still shown in the same treatment', async () => {
    createComponent('c1');
    await settle();

    httpMock.expectOne('/api/customers/c1/summary').flush(
      customerSummaryDto({
        firstName: 'Anonymized',
        lastName: 'Customer',
        email: 'erased-c1@anonymized.local',
        phoneNumber: '0000000000',
        isAnonymized: true,
      }),
    );
    await settle();

    const heading = fixture.nativeElement.querySelector('h1');
    expect(heading.textContent.trim()).toBe('Customer (anonymized)');
    expect(heading.className).toContain('italic');
    expect(heading.className).toContain('text-anonymized-text');

    const ddEls: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('dd'));
    const emailDd = ddEls.find((dd) => dd.textContent?.includes('erased-c1@anonymized.local'));
    expect(emailDd).toBeTruthy();
    expect(emailDd!.className).toContain('italic');
    expect(emailDd!.className).toContain('text-anonymized-text');

    const phoneDd = ddEls.find((dd) => dd.textContent?.trim() === '0000000000');
    expect(phoneDd).toBeTruthy();
    expect(phoneDd!.className).toContain('italic');
    expect(phoneDd!.className).toContain('text-anonymized-text');
  });

  it('clicking the convenience Print button calls window.print()', async () => {
    createComponent('c1');
    await settle();

    httpMock.expectOne('/api/customers/c1/summary').flush(customerSummaryDto());
    await settle();

    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => {});

    const printButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Print');
    expect(printButton).toBeTruthy();

    printButton!.click();

    expect(printSpy).toHaveBeenCalled();
  });

  it('flags the Print button and Back to Customers link as no-print (hidden from printed output)', async () => {
    createComponent('c1');
    await settle();

    httpMock.expectOne('/api/customers/c1/summary').flush(customerSummaryDto());
    await settle();

    const printButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Print');
    const backLink: HTMLAnchorElement = fixture.nativeElement.querySelector('a');

    expect(printButton!.className).toContain('no-print');
    expect(backLink.className).toContain('no-print');
  });
});
