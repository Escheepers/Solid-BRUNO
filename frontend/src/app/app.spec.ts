import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { App } from './app';
import { routes } from './app.routes';
import { apiKeyInterceptor } from './core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from './core/api-client/error-normalization.interceptor';

describe('App', () => {
  beforeEach(async () => {
    // `provideHttpClient`/`provideTanStackQuery` are needed now that "/" redirects
    // to "/bookings", which since spec-4-1 renders the real `BookingsPage` (a
    // TanStack Query + HttpClient consumer) rather than Story 1.6's static
    // placeholder text.
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(
          new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }),
        ),
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('hosts the persistent Top App Bar and a router outlet', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-top-app-bar')).toBeTruthy();
  });

  it('redirects "/" to "/bookings" and renders the Bookings placeholder', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    await TestBed.inject(Router).navigateByUrl('/');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Bookings');
  });
});
