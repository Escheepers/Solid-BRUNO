import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Toast } from './toast';
import { ToastService } from './toast.service';

describe('Toast', () => {
  let fixture: ComponentFixture<Toast>;
  let toastService: ToastService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Toast] }).compileComponents();
    fixture = TestBed.createComponent(Toast);
    toastService = TestBed.inject(ToastService);
  });

  function statusEls(): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[role="status"]'));
  }

  it('renders nothing when there are no active toasts', () => {
    fixture.detectChanges();
    expect(statusEls().length).toBe(0);
  });

  it("renders the service's current toasts with aria-live=polite", () => {
    toastService.success('Vehicle created.');
    fixture.detectChanges();

    expect(statusEls().length).toBe(1);
    expect(statusEls()[0].getAttribute('aria-live')).toBe('polite');
    expect(statusEls()[0].textContent).toContain('Vehicle created.');
  });

  it('renders one entry per active toast', () => {
    toastService.success('First');
    toastService.success('Second');
    fixture.detectChanges();

    expect(statusEls().length).toBe(2);
  });

  it('dismisses a toast when its close button is clicked', () => {
    toastService.success('Vehicle created.');
    fixture.detectChanges();

    fixture.nativeElement.querySelector('button[aria-label="Dismiss"]').click();
    fixture.detectChanges();

    expect(statusEls().length).toBe(0);
  });
});
