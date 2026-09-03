import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Skeleton } from './skeleton';

describe('Skeleton', () => {
  let fixture: ComponentFixture<Skeleton>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Skeleton] }).compileComponents();
    fixture = TestBed.createComponent(Skeleton);
  });

  function placeholderRows(): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[data-testid="skeleton-row"]'));
  }

  it('renders 5 placeholder rows by default', () => {
    fixture.detectChanges();
    expect(placeholderRows().length).toBe(5);
  });

  it('renders the number of placeholder rows given by the rows input', () => {
    fixture.componentRef.setInput('rows', 8);
    fixture.detectChanges();
    expect(placeholderRows().length).toBe(8);
  });

  it('sets aria-busy="true" on its host element', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.getAttribute('aria-busy')).toBe('true');
  });

  it('renders flat DESIGN.md-token placeholder blocks with no shimmer/animation classes', () => {
    fixture.detectChanges();
    const row = placeholderRows()[0];
    expect(row.className).toContain('bg-surface-alt');
    expect(row.className).toContain('rounded-sm');
    expect(row.className).not.toMatch(/animate|shimmer/);
  });
});
