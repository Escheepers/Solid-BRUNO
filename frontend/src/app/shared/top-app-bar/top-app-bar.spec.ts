import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { TopAppBar } from './top-app-bar';

@Component({ selector: 'app-blank-page', template: '' })
class BlankPage {}

/**
 * `RouterLinkActive` applies its DOM updates (classes, `aria-current`) inside a
 * `queueMicrotask`, decoupled from the promise `Router.navigateByUrl` resolves with.
 * A macrotask flush guarantees every already-queued microtask (including that one)
 * has run before we assert on the DOM.
 */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('TopAppBar', () => {
  let fixture: ComponentFixture<TopAppBar>;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TopAppBar],
      providers: [
        provideRouter([
          { path: 'bookings', component: BlankPage },
          { path: 'vehicles', component: BlankPage },
          { path: 'customers', component: BlankPage },
        ]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TopAppBar);
    router = TestBed.inject(Router);
  });

  function links(): HTMLAnchorElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('a'));
  }

  function activeLinks(): HTMLAnchorElement[] {
    return links().filter((a) => a.getAttribute('aria-current') === 'page');
  }

  it('renders the Bookings, Vehicles, and Customers nav links in that order', () => {
    fixture.detectChanges();
    const labels = links().map((a) => a.textContent?.trim());
    expect(labels).toEqual(['Bookings', 'Vehicles', 'Customers']);
  });

  it('marks exactly one nav link aria-current="page", matching the active route', async () => {
    await router.navigateByUrl('/vehicles');
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    const current = activeLinks();
    expect(current.length).toBe(1);
    expect(current[0].textContent?.trim()).toBe('Vehicles');
  });

  it('moves aria-current to the newly active link when the route changes', async () => {
    await router.navigateByUrl('/bookings');
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    expect(activeLinks().map((a) => a.textContent?.trim())).toEqual(['Bookings']);

    await router.navigateByUrl('/customers');
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    const current = activeLinks();
    expect(current.length).toBe(1);
    expect(current[0].textContent?.trim()).toBe('Customers');
  });

  it('marks the active link with the link color, bold weight, and underline classes (not color alone)', async () => {
    await router.navigateByUrl('/bookings');
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    const active = activeLinks()[0];
    // spec-6-4: text-link (not text-primary) -- {colors.primary}'s dark-mode
    // value fails AA contrast on this surface (3.58:1); {colors.link}'s
    // dark-mode value is the design's own token for primary-blue text
    // directly on a dark surface (7:1) and is value-identical to primary in
    // light mode, so this is a contrast fix with no light-mode visual change.
    expect(active.classList.contains('text-link')).toBe(true);
    expect(active.classList.contains('underline')).toBe(true);
    expect(Array.from(active.classList).some((c) => c.startsWith('font-bold'))).toBe(true);
  });
});
