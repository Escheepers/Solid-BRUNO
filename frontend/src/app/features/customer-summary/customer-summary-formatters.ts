/**
 * The single source of truth for how this page's currency/date fields are formatted.
 * Deliberately its own instance rather than importing another feature's formatters —
 * each feature folder stays a self-contained module with no cross-feature imports
 * (mirrors `features/bookings/booking-formatters.ts`'s exact precedent/reasoning for
 * its own duplicated instance). Same `en-ZA` config as every other formatter in the
 * app so every surface renders currency/dates identically despite the duplication.
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
