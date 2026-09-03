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
 * text, not a shared component DESIGN.md names.
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

  readonly pageChange = output<number>();

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
}
