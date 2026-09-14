import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Badge } from './badge';

describe('Badge', () => {
  let fixture: ComponentFixture<Badge>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Badge] }).compileComponents();
    fixture = TestBed.createComponent(Badge);
  });

  function dot(): HTMLElement {
    return fixture.nativeElement.querySelector('[data-testid="badge-dot"]');
  }

  it('renders the status text', () => {
    fixture.componentRef.setInput('status', 'Active');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Active');
  });

  it('renders a dot-first pill (the dot element precedes the text node)', () => {
    fixture.componentRef.setInput('status', 'Active');
    fixture.detectChanges();

    const pill = fixture.nativeElement.querySelector('span');
    expect(pill.firstElementChild).toBe(dot());
  });

  it('applies the success color tokens for Active', () => {
    fixture.componentRef.setInput('status', 'Active');
    fixture.detectChanges();

    const pill = fixture.nativeElement.querySelector('span');
    expect(pill.className).toContain('bg-success-bg');
    expect(pill.className).toContain('text-success-text');
    expect(dot().className).toContain('bg-success-dot');
  });

  it('applies the neutral color tokens for Completed', () => {
    fixture.componentRef.setInput('status', 'Completed');
    fixture.detectChanges();

    const pill = fixture.nativeElement.querySelector('span');
    expect(pill.className).toContain('bg-neutral-bg');
    expect(pill.className).toContain('text-neutral-text');
    expect(dot().className).toContain('bg-neutral-dot');
  });

  it('applies the danger color tokens for Cancelled', () => {
    fixture.componentRef.setInput('status', 'Cancelled');
    fixture.detectChanges();

    const pill = fixture.nativeElement.querySelector('span');
    expect(pill.className).toContain('bg-danger-bg');
    expect(pill.className).toContain('text-danger-text');
    expect(dot().className).toContain('bg-danger-dot');
  });

  it('uses the full-pill radius reserved for status badges only', () => {
    fixture.componentRef.setInput('status', 'Active');
    fixture.detectChanges();

    const pill = fixture.nativeElement.querySelector('span');
    expect(pill.className).toContain('rounded-full');
  });
});
