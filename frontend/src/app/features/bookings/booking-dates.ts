import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

const MS_PER_DAY = 24 * 60 * 60 * 1000;

/**
 * Whole-day difference between two `"yyyy-MM-dd"` date-input values, computed the same way the
 * backend computes it (`EndDate.DayNumber - StartDate.DayNumber`) -- anchoring both to UTC
 * midnight keeps the subtraction free of DST/timezone drift regardless of the browser's local
 * timezone.
 */
export function daysBetween(startIso: string, endIso: string): number {
  const start = new Date(`${startIso}T00:00:00Z`).getTime();
  const end = new Date(`${endIso}T00:00:00Z`).getTime();
  return Math.round((end - start) / MS_PER_DAY);
}

/** A local-calendar `Date` as `yyyy-MM-dd` (what `<input type="date">` uses). */
export function toIsoDate(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/** Today in the browser's local timezone as `yyyy-MM-dd`. */
export function todayIso(): string {
  return toIsoDate(new Date());
}

export function addDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

/** Rejects a (non-empty) start date earlier than today -- ISO dates compare correctly as strings. */
export const notInThePast: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = control.value as string;
  return value && value < todayIso() ? { pastDate: true } : null;
};
