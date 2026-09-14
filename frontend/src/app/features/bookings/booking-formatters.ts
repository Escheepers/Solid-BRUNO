/**
 * The single source of truth for how a `Booking`'s currency/date fields are
 * formatted. Deliberately its own instance rather than importing
 * `features/vehicles/vehicle-formatters.ts`'s `currencyFormatter`/`dateFormatter` --
 * each feature folder stays a self-contained module with no cross-feature imports
 * (mirrors `features/customers/customer-formatters.ts`'s exact precedent/reasoning
 * for its own duplicated `dateFormatter`). Same `en-ZA` config as both existing
 * formatters so every surface in the app renders currency/dates identically despite
 * the duplication.
 */
export const currencyFormatter = new Intl.NumberFormat('en-ZA', {
  style: 'currency',
  currency: 'ZAR',
});

export const dateFormatter = new Intl.DateTimeFormat('en-ZA', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
});
