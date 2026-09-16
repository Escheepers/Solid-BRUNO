import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import { Combobox } from './combobox';

interface Fruit {
  id: string;
  label: string;
}

const FRUITS: Fruit[] = [
  { id: '1', label: 'Apple' },
  { id: '2', label: 'Banana' },
  { id: '3', label: 'Cherry' },
];

@Component({
  selector: 'app-combobox-test-host',
  imports: [ReactiveFormsModule, Combobox],
  template: `
    <div (keydown)="onOuterKeydown()">
      <form [formGroup]="form">
        <label for="combo">Fruit</label>
        <app-combobox
          controlId="combo"
          formControlName="fruitId"
          [options]="options()"
          [optionLabel]="labelOf"
          [optionValue]="valueOf"
          [error]="error()"
        />
      </form>
    </div>
  `,
})
class TestHost {
  options = signal<Fruit[]>(FRUITS);
  form = new FormGroup({ fruitId: new FormControl('') });
  error = signal<string | null | undefined>(null);
  outerKeydownCount = 0;

  labelOf = (fruit: Fruit): string => fruit.label;
  valueOf = (fruit: Fruit): string => fruit.id;

  onOuterKeydown(): void {
    this.outerKeydownCount++;
  }
}

describe('Combobox', () => {
  let fixture: ComponentFixture<TestHost>;
  let host: TestHost;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TestHost] }).compileComponents();
    fixture = TestBed.createComponent(TestHost);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  function textInput(): HTMLInputElement {
    return fixture.nativeElement.querySelector('#combo');
  }

  function listbox(): HTMLUListElement | null {
    return fixture.nativeElement.querySelector('[role="listbox"]');
  }

  function optionEls(): HTMLLIElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[role="option"]'));
  }

  function type(text: string): void {
    const el = textInput();
    el.value = text;
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function keydown(key: string, extra: Partial<KeyboardEventInit> = {}): void {
    textInput().dispatchEvent(
      new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...extra }),
    );
    fixture.detectChanges();
  }

  function clickOption(value: string): void {
    if (!listbox()) {
      keydown('ArrowDown');
    }
    const option = optionEls().find((o) => o.getAttribute('data-value') === value)!;
    option.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    option.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  describe('ARIA structure', () => {
    it('renders a real combobox input with the List Autocomplete attributes', () => {
      const el = textInput();
      expect(el.getAttribute('role')).toBe('combobox');
      expect(el.getAttribute('aria-autocomplete')).toBe('list');
      expect(el.getAttribute('aria-expanded')).toBe('false');
      expect(el.getAttribute('aria-controls')).toBeTruthy();
    });

    it('does not render the listbox popup while closed', () => {
      expect(listbox()).toBeNull();
    });

    it('renders role="option" children inside a role="listbox" popup once opened, matching aria-controls', () => {
      keydown('ArrowDown');

      const box = listbox();
      expect(box).not.toBeNull();
      expect(textInput().getAttribute('aria-controls')).toBe(box!.id);
      expect(optionEls().length).toBe(FRUITS.length);
      expect(optionEls().every((o) => o.getAttribute('role') === 'option')).toBe(true);
    });

    it('tracks the active option via aria-activedescendant, not just visual highlighting', () => {
      keydown('ArrowDown');

      const activeId = textInput().getAttribute('aria-activedescendant');
      expect(activeId).toBeTruthy();
      expect(optionEls().some((o) => o.id === activeId)).toBe(true);
    });

    it('sets aria-invalid and aria-describedby from the error input, mirroring Input', () => {
      expect(textInput().getAttribute('aria-describedby')).toBe('combo-error');
      expect(textInput().hasAttribute('aria-invalid')).toBe(false);

      host.error.set('Required.');
      fixture.detectChanges();

      expect(textInput().getAttribute('aria-invalid')).toBe('true');
    });
  });

  describe('filtering', () => {
    it('filters the option list live, case-insensitively, as the user types', () => {
      type('an');

      const labels = optionEls().map((o) => o.textContent?.trim());
      expect(labels).toEqual(['Banana']);
    });

    it('shows a no-matches state and no selectable option when nothing matches', () => {
      type('zzz');

      expect(optionEls().length).toBe(0);
      expect(listbox()!.textContent).toContain('No matches');
    });

    it('Enter is a no-op (no crash, selection unchanged) when the filter matches nothing', () => {
      type('zzz');

      keydown('Enter');

      expect(host.form.controls.fruitId.value).toBe('');
      expect(listbox()).not.toBeNull();
    });

    it('ArrowDown/ArrowUp are no-ops when the filter matches nothing', () => {
      type('zzz');

      keydown('ArrowDown');
      keydown('ArrowUp');

      expect(textInput().getAttribute('aria-activedescendant')).toBeNull();
    });
  });

  describe('keyboard navigation', () => {
    it('ArrowDown opens the popup showing every option when nothing has been typed', () => {
      expect(listbox()).toBeNull();

      keydown('ArrowDown');

      expect(listbox()).not.toBeNull();
      expect(optionEls().length).toBe(FRUITS.length);
    });

    it('ArrowDown then Enter selects the active option, closes the popup, and updates the form control', () => {
      keydown('ArrowDown');
      keydown('Enter');

      expect(host.form.controls.fruitId.value).toBe('1');
      expect(listbox()).toBeNull();
      expect(textInput().value).toBe('Apple');
    });

    it('ArrowDown moves the active option forward before Enter commits it', () => {
      keydown('ArrowDown');
      keydown('ArrowDown');
      keydown('Enter');

      expect(host.form.controls.fruitId.value).toBe('2');
    });

    it('opening via ArrowDown when a value is already selected activates that option, not the first one', () => {
      clickOption('3');

      keydown('ArrowDown');

      const activeId = textInput().getAttribute('aria-activedescendant');
      const activeOption = optionEls().find((o) => o.id === activeId);
      expect(activeOption?.textContent?.trim()).toBe('Cherry');
    });
  });

  describe('Escape', () => {
    it('closes the popup and reverts the visible text to the selected option label without changing the selection', () => {
      clickOption('2');
      expect(host.form.controls.fruitId.value).toBe('2');

      type('something else entirely');
      expect(listbox()).not.toBeNull();

      keydown('Escape');

      expect(listbox()).toBeNull();
      expect(textInput().value).toBe('Banana');
      expect(host.form.controls.fruitId.value).toBe('2');
    });

    it('does not bubble to an ancestor keydown listener while the popup is open (so it never also closes an enclosing Modal)', () => {
      keydown('ArrowDown');
      host.outerKeydownCount = 0;

      keydown('Escape');

      expect(host.outerKeydownCount).toBe(0);
    });

    it('bubbles normally when the popup is already closed', () => {
      host.outerKeydownCount = 0;

      keydown('Escape');

      expect(host.outerKeydownCount).toBe(1);
    });
  });

  describe('blur', () => {
    it('closes the popup without altering the selection, and reverts the visible text', () => {
      clickOption('1');
      type('xyz');
      expect(listbox()).not.toBeNull();

      textInput().dispatchEvent(new Event('blur'));
      fixture.detectChanges();

      expect(listbox()).toBeNull();
      expect(host.form.controls.fruitId.value).toBe('1');
      expect(textInput().value).toBe('Apple');
    });

    it('marks the control as touched', () => {
      expect(host.form.controls.fruitId.touched).toBe(false);

      textInput().dispatchEvent(new Event('blur'));

      expect(host.form.controls.fruitId.touched).toBe(true);
    });
  });

  describe('ControlValueAccessor contract', () => {
    it('clicking an option updates the bound FormControl (registerOnChange)', () => {
      clickOption('3');

      expect(host.form.controls.fruitId.value).toBe('3');
    });

    it('setting the FormControl value externally (writeValue) updates the displayed text', () => {
      host.form.controls.fruitId.setValue('2');
      fixture.detectChanges();

      expect(textInput().value).toBe('Banana');
    });

    it('resolves the displayed text once options finish loading, even when writeValue ran first', () => {
      host.options.set([]);
      fixture.detectChanges();

      host.form.controls.fruitId.setValue('2');
      fixture.detectChanges();

      // Options haven't arrived yet -- never stuck showing stale/garbage text.
      expect(textInput().value).toBe('');

      host.options.set(FRUITS);
      fixture.detectChanges();

      expect(textInput().value).toBe('Banana');
    });

    it('disables the underlying input when the FormControl is disabled', () => {
      host.form.controls.fruitId.disable();
      fixture.detectChanges();

      expect(textInput().disabled).toBe(true);
    });
  });
});
