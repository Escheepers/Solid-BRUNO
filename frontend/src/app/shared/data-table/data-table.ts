import { NgTemplateOutlet } from '@angular/common';
import { Component, TemplateRef, computed, effect, input, linkedSignal, output } from '@angular/core';

import { Skeleton } from '../skeleton/skeleton';

/**
 * One column of a `DataTable`. `cell` is a plain render function rather than an
 * Angular structural-directive template — sufficient for every all-primitive-field
 * entity so far (including currency-formatted values) and simpler to unit test; see
 * spec-1-7's Design Notes for when this should be revisited.
 */
export interface ColumnDef<T> {
  header: string;
  cell: (row: T) => string;
  /**
   * Optional per-row class override for this column's `<td>` (spec-3-5's Scope
   * decision 4, added for the anonymized-customer name treatment). Omitted by
   * every pre-existing column (defaults to no-op), so this is additive only --
   * when it returns a non-empty string for a row, it replaces that cell's default
   * `text-text-body`/`text-text-disabled` treatment rather than layering on top of
   * it (avoids two conflicting Tailwind text-color utilities on the same element).
   */
  cellClass?: (row: T) => string;
  /**
   * Optional Angular template rendered in place of the plain-text `cell()` output
   * (spec-4-1's Scope decision 6/`ColumnDef.cell`'s own "see spec-1-7's Design
   * Notes for when this should be revisited" trigger) -- first needed by Bookings'
   * Status column, which renders a real `Badge` component per row rather than a
   * string. `cell` stays required regardless (a plain-text fallback always exists,
   * no special-casing needed), and `cellTemplate` is additive-only: every
   * pre-existing caller (Vehicles/Customers, no `cellTemplate`) renders exactly as
   * before via the `cell()` branch.
   */
  cellTemplate?: TemplateRef<{ $implicit: T }>;
  /**
   * Marks this column as sortable (spec-6-4's Accessibility Verification Pass --
   * `EXPERIENCE.md`'s Component Patterns: "Sortable by clicking a column header
   * where meaningful (date, price) -- the header is a real `<button>` with
   * `aria-sort` on the `<th>`"). Only date/price columns opt in (per spec-6-4's
   * Boundaries: "Only columns where EXPERIENCE.md calls sorting meaningful ...
   * get `sortable: true` -- not every column"); every pre-existing column omits
   * this and renders exactly as before (additive only).
   */
  sortable?: boolean;
  /**
   * The raw value `DataTable` sorts by when `sortable` is true -- distinct from
   * `cell()`'s already-formatted display string (e.g. `"R1,400.00"`,
   * `"1 Oct 2026"`), neither of which sorts correctly as plain text (currency
   * grouping and month names both break lexicographic order). Falls back to
   * `cell(row)` when omitted, which is only correct for an already-sortable
   * plain-text column -- every current `sortable: true` column (date/price)
   * supplies this explicitly.
   */
  sortValue?: (row: T) => string | number;
}

/** The two directions a sortable column can be actively sorted in; `aria-sort`
 * also uses the literal string `'none'` for a sortable-but-inactive column,
 * handled separately in `DataTable.ariaSortFor`. */
type SortDirection = 'ascending' | 'descending';

/**
 * One row-level action rendered as a trailing text-link cell (spec-2-2's Scope
 * decision 2). Deliberately generic — `DataTable` has no idea what "Edit" (or any
 * other action) means (SRP); the caller supplies the label and the click handler.
 */
export interface RowAction<T> {
  label: string;
  onClick: (row: T) => void;
}

/**
 * The one generic, reusable table (`{components.data-table}` in DESIGN.md), built
 * against its first real consumer (Vehicles, Story 1.7) rather than speculatively
 * ahead of usage. Zebra striping, uppercase `section-label` headers, and the
 * pagination footer are all built in here — `DESIGN.md` explicitly calls the pager
 * "page furniture local to `DataTable`, styled inline with it," not a standalone
 * component. Renders `Skeleton` in place of the table (never both at once) while
 * `loading` is true. Does not handle empty states itself — a `DataTable` with zero
 * rows and `loading=false` is the caller's concern (e.g. Vehicles' two distinct
 * empty-state messages), since the two states this story needs are entity-specific
 * text, not a shared component DESIGN.md names. `actions` (spec-2-2, changed to a
 * per-row function in spec-2-4) adds an optional trailing column of generic
 * row-level text-link actions — rendered only when at least one row produces at
 * least one action, so every pre-existing caller (columns/rows/etc. only) keeps
 * working unchanged. `rowMuted`/`rowKey`+`rowError` (spec-2-4) stay entity-agnostic
 * too: `DataTable` never learns what "soft-deleted" or "already active" means, only
 * renders what the caller's predicates/functions tell it to (SRP). `ColumnDef.sortable`
 * (spec-6-4) adds an optional, per-page client-side sort over whichever column opts
 * in -- see `ColumnDef.sortable`'s own doc comment; every pre-existing column/caller
 * omits it and is unaffected.
 */
@Component({
  selector: 'app-data-table',
  imports: [Skeleton, NgTemplateOutlet],
  templateUrl: './data-table.html',
})
export class DataTable<T> {
  readonly columns = input.required<ColumnDef<T>[]>();
  readonly rows = input.required<T[]>();
  readonly loading = input(false);
  readonly page = input(1);
  readonly pageSize = input(20);
  readonly totalCount = input(0);
  readonly actions = input<(row: T) => RowAction<T>[]>(() => []);
  readonly rowMuted = input<((row: T) => boolean) | null>(null);
  readonly rowKey = input<((row: T) => string) | null>(null);
  readonly rowError = input<{ key: string; message: string } | null>(null);
  /**
   * Changing this value clears any column sort the user has clicked (spec-new-rows-visible-first):
   * the owner bumps it after a create so the server's newest-first order shows again and the new
   * row isn't buried by a sort chosen earlier. Its value has no meaning beyond "it changed".
   */
  readonly sortResetToken = input(0);

  readonly pageChange = output<number>();

  constructor() {
    // A page past the last one (the last row of the last page was just removed, or a filter shrank
    // the result set) would otherwise leave an empty table under a non-zero count. Ask the owner to
    // move to the last valid page. Skipped while loading and when there are no rows at all -- the
    // latter is the owner's empty state, not a stale page number.
    effect(() => {
      if (this.loading() || this.totalCount() === 0) {
        return;
      }
      const lastPage = this.totalPages();
      if (this.page() > lastPage) {
        this.pageChange.emit(lastPage);
      }
    });
  }

  /**
   * The single sortable column/direction active at a time (or `null` for the
   * unsorted, as-received order) -- clicking a column header cycles
   * none -> ascending -> descending -> none per spec-6-4's Boundaries.
   * Deliberately keyed by `column.header` (already this table's per-column
   * identity, per the existing `track column.header`) rather than a column
   * index, so it survives `columns()` re-creation across change detection.
   * A `linkedSignal` on `sortResetToken` so the owner can clear it from outside.
   */
  private readonly sortState = linkedSignal<number, { header: string; direction: SortDirection } | null>({
    source: this.sortResetToken,
    computation: () => null,
  });

  /**
   * `rows()` sorted per `sortState` -- client-side, over only the currently
   * loaded page (spec-6-4's Design Notes: proportional to a verification pass,
   * not new backend query params). Falls through to `rows()` unchanged when
   * nothing is sorted, or when the sorted-by column is no longer present/
   * sortable (defensive; `columns()` is caller-supplied and could change).
   */
  protected readonly sortedRows = computed<T[]>(() => {
    const rows = this.rows();
    const state = this.sortState();
    if (!state) {
      return rows;
    }

    const column = this.columns().find((c) => c.header === state.header && c.sortable);
    if (!column) {
      return rows;
    }

    const sign = state.direction === 'ascending' ? 1 : -1;
    return [...rows].sort((a, b) => sign * this.compareRows(column, a, b));
  });

  protected readonly hasAnyRowActions = computed(() =>
    this.rows().some((row) => this.actions()(row).length > 0),
  );

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())),
  );

  protected readonly rangeStart = computed(() =>
    this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1,
  );

  protected readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount()),
  );

  protected goToPrevious(): void {
    this.goToPage(this.page() - 1);
  }

  protected goToNext(): void {
    this.goToPage(this.page() + 1);
  }

  private goToPage(target: number): void {
    if (target < 1 || target > this.totalPages() || target === this.page()) {
      return;
    }
    this.pageChange.emit(target);
  }

  protected isRowMuted(row: T): boolean {
    return this.rowMuted()?.(row) ?? false;
  }

  protected cellClass(column: ColumnDef<T>, row: T): string {
    return column.cellClass?.(row) ?? '';
  }

  protected rowErrorMessage(row: T): string | null {
    const key = this.rowKey();
    const error = this.rowError();
    if (!key || !error || key(row) !== error.key) {
      return null;
    }
    return error.message;
  }

  /**
   * Cycles this column's sort state: unsorted -> ascending -> descending ->
   * unsorted (spec-6-4's Boundaries: "Ascending/descending/none cycle").
   * Clicking a different sortable column while one is already active replaces
   * it outright (only one column is ever sorted at a time) rather than
   * stacking a multi-column sort, which nothing in this spec or `EXPERIENCE.md`
   * asks for (YAGNI). A no-op for a non-sortable column -- guards the case
   * where this is reached via the header's plain-text branch having no click
   * handler at all, kept here too as defense-in-depth.
   */
  protected onSortClick(column: ColumnDef<T>): void {
    if (!column.sortable) {
      return;
    }

    const current = this.sortState();
    if (!current || current.header !== column.header) {
      this.sortState.set({ header: column.header, direction: 'ascending' });
      return;
    }

    if (current.direction === 'ascending') {
      this.sortState.set({ header: column.header, direction: 'descending' });
      return;
    }

    this.sortState.set(null);
  }

  /** `null` for a non-sortable column (no `aria-sort` attribute at all --
   * `attr.aria-sort` binding to `null` omits it entirely); `'none'` for a
   * sortable column that isn't the active one; otherwise the active
   * direction. Mirrors the exact `aria-sort` values `EXPERIENCE.md`/WAI-ARIA
   * expect on a sortable `<th>`. */
  protected ariaSortFor(column: ColumnDef<T>): SortDirection | 'none' | null {
    if (!column.sortable) {
      return null;
    }

    const state = this.sortState();
    return state && state.header === column.header ? state.direction : 'none';
  }

  /** A persistent (not hover-only) visual indicator per spec-6-4's Boundaries --
   * a neutral two-way glyph on a sortable-but-inactive column (so it reads as
   * sortable at a glance), replaced by a directional arrow once active. */
  protected sortIndicator(column: ColumnDef<T>): string {
    const state = this.sortState();
    if (!state || state.header !== column.header) {
      return '⇅';
    }
    return state.direction === 'ascending' ? '▲' : '▼';
  }

  private compareRows(column: ColumnDef<T>, a: T, b: T): number {
    const getValue = column.sortValue ?? column.cell;
    return this.compareValues(getValue(a), getValue(b));
  }

  private compareValues(a: string | number, b: string | number): number {
    if (typeof a === 'number' && typeof b === 'number') {
      return a - b;
    }
    return String(a).localeCompare(String(b));
  }
}
