import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import { Input } from './input';

@Component({
  selector: 'app-input-test-host',
  imports: [ReactiveFormsModule, Input],
  template: `
    <form [formGroup]="form">
      <app-input formControlName="make" label="Make" [error]="error()" />
    </form>
  `,
})
class TestHost {
  form = new FormGroup({ make: new FormControl('') });
  error = signal<string | null | undefined>(null);
}

describe('Input', () => {
  let fixture: ComponentFixture<TestHost>;
  let host: TestHost;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TestHost] }).compileComponents();
    fixture = TestBed.createComponent(TestHost);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  function nativeInput(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input');
  }

  it('renders the label', () => {
    expect(fixture.nativeElement.querySelector('label').textContent).toContain('Make');
  });

  it('typing in the underlying input updates the bound FormControl value', () => {
    const input = nativeInput();
    input.value = 'Toyota';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(host.form.controls.make.value).toBe('Toyota');
  });

  it('setting the FormControl value externally updates the displayed input value', () => {
    host.form.controls.make.setValue('Ford');
    fixture.detectChanges();

    expect(nativeInput().value).toBe('Ford');
  });

  it('disables the underlying input when the FormControl is disabled', () => {
    host.form.controls.make.disable();
    fixture.detectChanges();

    expect(nativeInput().disabled).toBe(true);
  });

  it('renders the error text when the error input is a non-empty string', () => {
    host.error.set('Make is required.');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Make is required.');
  });

  it('renders no error text when the error input is absent', () => {
    host.error.set(undefined);
    fixture.detectChanges();

    const liveRegion = fixture.nativeElement.querySelector('[aria-live="polite"]');
    expect(liveRegion?.textContent?.trim()).toBe('');
  });

  it('renders no error text when the error input is an empty string', () => {
    host.error.set('');
    fixture.detectChanges();

    const liveRegion = fixture.nativeElement.querySelector('[aria-live="polite"]');
    expect(liveRegion?.textContent?.trim()).toBe('');
  });
});
