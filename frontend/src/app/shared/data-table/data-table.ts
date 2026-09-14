import { Component, computed, input, output } from '@angular/core';

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
}

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
 * renders what the caller's predicates/functions tell it to (SRP).
 */
@Component({
  selector: 'app-data-table',
  imports: [Skeleton],
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

  readonly pageChange = output<number>();

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
}
