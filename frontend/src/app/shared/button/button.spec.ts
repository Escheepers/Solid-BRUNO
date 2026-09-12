import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Button } from './button';

@Component({
  selector: 'app-button-test-host',
  imports: [Button],
  template: `<app-button [loading]="loading" [disabled]="disabled">Save</app-button>`,
})
class TestHost {
  loading = false;
  disabled = false;
}

describe('Button', () => {
  let fixture: ComponentFixture<TestHost>;
  let host: TestHost;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TestHost] }).compileComponents();
    fixture = TestBed.createComponent(TestHost);
    host = fixture.componentInstance;
  });

  function button(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('button');
  }

  it('shows the projected label and is enabled when loading and disabled are both false', () => {
    fixture.detectChanges();

    expect(button().disabled).toBe(false);
    expect(button().textContent).toContain('Save');
  });

  it('disables the button and shows a spinner in place of the label when loading is true', () => {
    host.loading = true;
    fixture.detectChanges();

    expect(button().disabled).toBe(true);
    expect(button().textContent).not.toContain('Save');
    expect(fixture.nativeElement.querySelector('.animate-spin')).not.toBeNull();
  });

  it('defaults to type="button"', () => {
    fixture.detectChanges();
    expect(button().type).toBe('button');
  });

  it('stays disabled when disabled is explicitly true, even while not loading', () => {
    host.disabled = true;
    fixture.detectChanges();

    expect(button().disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('.animate-spin')).toBeNull();
  });
});
