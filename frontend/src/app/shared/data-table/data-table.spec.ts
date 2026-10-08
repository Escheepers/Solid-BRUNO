import { Component, TemplateRef, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ColumnDef, DataTable, RowAction } from './data-table';

interface Row {
  id: number;
  name: string;
}

describe('DataTable', () => {
  let fixture: ComponentFixture<DataTable<Row>>;
  let component: DataTable<Row>;

  const columns: ColumnDef<Row>[] = [
    { header: 'ID', cell: (row) => String(row.id) },
    { header: 'Name', cell: (row) => row.name.toUpperCase() },
  ];

  const rows: Row[] = [
    { id: 1, name: 'alpha' },
    { id: 2, name: 'beta' },
    { id: 3, name: 'gamma' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DataTable] }).compileComponents();
    fixture = TestBed.createComponent<DataTable<Row>>(DataTable);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('page', 1);
    fixture.componentRef.setInput('pageSize', 20);
    fixture.componentRef.setInput('totalCount', rows.length);
  });

  function tableRows(): HTMLTableRowElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('tbody tr'));
  }

  function buttons(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('button'));
  }

  function buttonNamed(label: string): HTMLButtonElement | undefined {
    return buttons().find((b) => b.textContent?.trim() === label);
  }

  it('renders one table row per data row', () => {
    fixture.detectChanges();
    expect(tableRows().length).toBe(3);
  });

  it('renders each column header from the column definitions', () => {
    fixture.detectChanges();
    const headers = Array.from(fixture.nativeElement.querySelectorAll('th')).map((th) =>
      (th as HTMLElement).textContent?.trim(),
    );
    expect(headers).toEqual(['ID', 'Name']);
  });

  it("calls each column's cell() function to render cell content", () => {
    fixture.detectChanges();
    const firstRowCells = tableRows()[0].querySelectorAll('td');
    expect(firstRowCells[0].textContent?.trim()).toBe('1');
    expect(firstRowCells[1].textContent?.trim()).toBe('ALPHA');
  });

  it('renders Skeleton instead of the table when loading is true', () => {
    fixture.componentRef.setInput('loading', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-skeleton')).not.toBeNull();
  });

  it('does not render Skeleton when not loading', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-skeleton')).toBeNull();
    expect(fixture.nativeElement.querySelector('table')).not.toBeNull();
  });

  it('emits pageChange with the next page number when Next is clicked', () => {
    fixture.componentRef.setInput('totalCount', 100);
    fixture.detectChanges();

    let emitted: number | undefined;
    component.pageChange.subscribe((page) => (emitted = page));

    buttonNamed('Next')?.click();

    expect(emitted).toBe(2);
  });

  it('emits pageChange with the previous page number when Previous is clicked', () => {
    fixture.componentRef.setInput('totalCount', 100);
    fixture.componentRef.setInput('page', 2);
    fixture.detectChanges();

    let emitted: number | undefined;
    component.pageChange.subscribe((page) => (emitted = page));

    buttonNamed('Previous')?.click();

    expect(emitted).toBe(1);
  });

  it('does not emit pageChange when Previous is clicked on page 1', () => {
    fixture.detectChanges();
    let emitted = false;
    component.pageChange.subscribe(() => (emitted = true));

    buttonNamed('Previous')?.click();

    expect(emitted).toBe(false);
  });

  it('does not emit pageChange when Next is clicked on the last page', () => {
    fixture.detectChanges(); // 3 rows, pageSize 20 -> only 1 page
    let emitted = false;
    component.pageChange.subscribe(() => (emitted = true));

    buttonNamed('Next')?.click();

    expect(emitted).toBe(false);
  });

  it('shows a "Showing X-Y of Z" summary reflecting the current page', () => {
    fixture.componentRef.setInput('totalCount', 45);
    fixture.componentRef.setInput('page', 2);
    fixture.componentRef.setInput('pageSize', 20);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('21');
    expect(fixture.nativeElement.textContent).toContain('40');
    expect(fixture.nativeElement.textContent).toContain('45');
  });

  it('the "Showing X-Y of Z" summary carries aria-live="polite" (spec-6-4: FilterBar/DataTable result count)', () => {
    fixture.detectChanges();

    const region = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('span')).find(
      (span) => span.textContent?.includes('Showing'),
    );
    expect(region?.getAttribute('aria-live')).toBe('polite');
  });

  it('does not render an actions column when no actions are provided', () => {
    fixture.detectChanges();

    const headers = Array.from(fixture.nativeElement.querySelectorAll('thead th'));
    expect(headers.length).toBe(columns.length);
    expect(tableRows()[0].querySelectorAll('td').length).toBe(columns.length);
  });

  it('renders a trailing actions column and calls the action onClick with the correct row when clicked', () => {
    const onClick = vi.fn();
    const actions: RowAction<Row>[] = [{ label: 'Edit', onClick }];
    fixture.componentRef.setInput('actions', () => actions);
    fixture.detectChanges();

    const headers = Array.from(fixture.nativeElement.querySelectorAll('thead th'));
    expect(headers.length).toBe(columns.length + 1);
    expect(tableRows()[0].querySelectorAll('td').length).toBe(columns.length + 1);

    const firstRowActionButton = tableRows()[0].querySelector(
      'td:last-child button',
    ) as HTMLButtonElement;
    expect(firstRowActionButton.textContent?.trim()).toBe('Edit');

    firstRowActionButton.click();

    expect(onClick).toHaveBeenCalledWith(rows[0]);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('renders multiple actions per row', () => {
    const editClick = vi.fn();
    const deactivateClick = vi.fn();
    const actions: RowAction<Row>[] = [
      { label: 'Edit', onClick: editClick },
      { label: 'Deactivate', onClick: deactivateClick },
    ];
    fixture.componentRef.setInput('actions', () => actions);
    fixture.detectChanges();

    const secondRowButtons = Array.from<HTMLButtonElement>(
      tableRows()[1].querySelectorAll('td:last-child button'),
    );
    expect(secondRowButtons.map((b) => b.textContent?.trim())).toEqual(['Edit', 'Deactivate']);

    secondRowButtons[1].click();
    expect(deactivateClick).toHaveBeenCalledWith(rows[1]);
    expect(editClick).not.toHaveBeenCalled();
  });

  it('calls the actions function per row, allowing different action sets for different rows', () => {
    const restoreClick = vi.fn();
    const editClick = vi.fn();
    fixture.componentRef.setInput('actions', (row: Row) =>
      row.id === 2
        ? [{ label: 'Restore', onClick: restoreClick }]
        : [{ label: 'Edit', onClick: editClick }],
    );
    fixture.detectChanges();

    const firstRowButtons = Array.from<HTMLButtonElement>(
      tableRows()[0].querySelectorAll('td:last-child button'),
    );
    const secondRowButtons = Array.from<HTMLButtonElement>(
      tableRows()[1].querySelectorAll('td:last-child button'),
    );

    expect(firstRowButtons.map((b) => b.textContent?.trim())).toEqual(['Edit']);
    expect(secondRowButtons.map((b) => b.textContent?.trim())).toEqual(['Restore']);
  });

  it('does not render an actions column when the actions function returns an empty array for every row', () => {
    fixture.componentRef.setInput('actions', () => []);
    fixture.detectChanges();

    const headers = Array.from(fixture.nativeElement.querySelectorAll('thead th'));
    expect(headers.length).toBe(columns.length);
    expect(tableRows()[0].querySelectorAll('td').length).toBe(columns.length);
  });

  describe('rowMuted', () => {
    it('applies a dimmed treatment only to rows for which the predicate returns true', () => {
      fixture.componentRef.setInput('rowMuted', (row: Row) => row.id === 2);
      fixture.detectChanges();

      const mutedRowCell = tableRows()[1].querySelector('td') as HTMLElement;
      const normalRowCell = tableRows()[0].querySelector('td') as HTMLElement;

      expect(mutedRowCell.className).toContain('text-text-disabled');
      expect(mutedRowCell.className).not.toContain('text-text-body');
      expect(normalRowCell.className).toContain('text-text-body');
      expect(normalRowCell.className).not.toContain('text-text-disabled');
    });

    it('applies no dimmed treatment to any row when rowMuted is not provided', () => {
      fixture.detectChanges();

      for (const row of tableRows()) {
        const cell = row.querySelector('td') as HTMLElement;
        expect(cell.className).toContain('text-text-body');
      }
    });
  });

  describe('rowKey / rowError', () => {
    it('renders the inline error message and error-row background only on the matching row', () => {
      fixture.componentRef.setInput('rowKey', (row: Row) => String(row.id));
      fixture.componentRef.setInput('rowError', { key: '2', message: 'Already active.' });
      fixture.componentRef.setInput('actions', () => [{ label: 'Restore', onClick: vi.fn() }]);
      fixture.detectChanges();

      expect(tableRows()[0].textContent).not.toContain('Already active.');
      expect(tableRows()[0].className).not.toContain('bg-error-row-bg');

      expect(tableRows()[1].textContent).toContain('Already active.');
      expect(tableRows()[1].className).toContain('bg-error-row-bg');

      expect(tableRows()[2].textContent).not.toContain('Already active.');
      expect(tableRows()[2].className).not.toContain('bg-error-row-bg');
    });

    it('renders no inline error on any row when rowError is null', () => {
      fixture.componentRef.setInput('rowKey', (row: Row) => String(row.id));
      fixture.componentRef.setInput('rowError', null);
      fixture.detectChanges();

      for (const row of tableRows()) {
        expect(row.className).not.toContain('bg-error-row-bg');
      }
    });
  });

  describe('cellTemplate', () => {
    @Component({
      selector: 'app-data-table-template-host',
      imports: [DataTable],
      template: `
        <ng-template #nameCell let-row>
          <strong data-testid="template-cell">{{ row.name }}!!!</strong>
        </ng-template>
        <app-data-table
          [columns]="columns"
          [rows]="rows"
          [page]="1"
          [pageSize]="20"
          [totalCount]="rows.length"
        />
      `,
    })
    class TemplateHost {
      @ViewChild('nameCell', { static: true })
      private readonly nameCellRef!: TemplateRef<{ $implicit: Row }>;

      readonly rows: Row[] = rows;
      columns!: ColumnDef<Row>[];

      ngOnInit(): void {
        this.columns = [
          { header: 'ID', cell: (row) => String(row.id) },
          { header: 'Name', cell: (row) => row.name, cellTemplate: this.nameCellRef },
        ];
      }
    }

    it("renders a column's cellTemplate instead of its plain-text cell() when provided", async () => {
      TestBed.resetTestingModule();
      await TestBed.configureTestingModule({ imports: [TemplateHost] }).compileComponents();
      const hostFixture = TestBed.createComponent(TemplateHost);
      hostFixture.detectChanges();

      const templateCells = Array.from<HTMLElement>(
        hostFixture.nativeElement.querySelectorAll('[data-testid="template-cell"]'),
      );
      expect(templateCells.length).toBe(rows.length);
      expect(templateCells[0].textContent).toContain('alpha!!!');

      // The ID column has no cellTemplate -- it keeps rendering via cell() untouched.
      const firstRowCells = hostFixture.nativeElement.querySelectorAll('tbody tr')[0]
        .querySelectorAll('td');
      expect(firstRowCells[0].textContent?.trim()).toBe('1');
    });

    it('every pre-existing column without cellTemplate still renders via cell() unchanged', () => {
      fixture.detectChanges();

      const firstRowCells = tableRows()[0].querySelectorAll('td');
      expect(firstRowCells[0].textContent?.trim()).toBe('1');
      expect(firstRowCells[1].textContent?.trim()).toBe('ALPHA');
    });
  });

  describe('cellClass', () => {
    it("applies a column's cellClass only to the rows for which it returns a non-empty string", () => {
      const columnsWithCellClass: ColumnDef<Row>[] = [
        { header: 'ID', cell: (row) => String(row.id) },
        {
          header: 'Name',
          cell: (row) => row.name.toUpperCase(),
          cellClass: (row) => (row.id === 2 ? 'italic text-anonymized-text' : ''),
        },
      ];
      fixture.componentRef.setInput('columns', columnsWithCellClass);
      fixture.detectChanges();

      const styledCell = tableRows()[1].querySelectorAll('td')[1] as HTMLElement;
      const plainCell = tableRows()[0].querySelectorAll('td')[1] as HTMLElement;

      expect(styledCell.className).toContain('italic');
      expect(styledCell.className).toContain('text-anonymized-text');

      expect(plainCell.className).not.toContain('italic');
      expect(plainCell.className).not.toContain('text-anonymized-text');
    });

    it('suppresses the default text-text-body/text-text-disabled classes on a cell whose cellClass is non-empty', () => {
      const columnsWithCellClass: ColumnDef<Row>[] = [
        {
          header: 'Name',
          cell: (row) => row.name,
          cellClass: (row) => (row.id === 2 ? 'italic text-anonymized-text' : ''),
        },
      ];
      fixture.componentRef.setInput('columns', columnsWithCellClass);
      fixture.detectChanges();

      const styledCell = tableRows()[1].querySelector('td') as HTMLElement;
      const plainCell = tableRows()[0].querySelector('td') as HTMLElement;

      expect(styledCell.className).not.toContain('text-text-body');
      expect(styledCell.className).not.toContain('text-text-disabled');
      expect(plainCell.className).toContain('text-text-body');
    });

    it('leaves every cell with the default text-text-body treatment when no column defines cellClass', () => {
      fixture.detectChanges();

      for (const row of tableRows()) {
        for (const cell of Array.from(row.querySelectorAll('td'))) {
          expect((cell as HTMLElement).className).toContain('text-text-body');
        }
      }
    });
  });

  /**
   * spec-6-4's Accessibility Verification Pass: a sortable column's header must
   * be a real, keyboard-activatable `<button>` with `aria-sort` reflecting
   * current state, cycling none -> ascending -> descending -> none, sorting
   * only the currently-loaded page of rows client-side. `EXPERIENCE.md`'s "Tab/
   * Enter/Space" keyboard-operability claim rests on this being a genuine
   * `<button>` element (Enter/Space-triggers-click is a browser default action
   * for `<button>`, not something this component's own code could get wrong,
   * and isn't reproducible in JSDOM -- verified live instead per this story's
   * Verification section); the tests below assert the button is real, focusable,
   * and non-disabled, and that clicking it (the same event a real Enter/Space
   * keypress produces) has the correct effect.
   */
  describe('sortable columns', () => {
    function header(label: string): HTMLElement {
      return Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('th')).find((th) =>
        th.textContent?.includes(label),
      )!;
    }

    function sortButton(label: string): HTMLButtonElement {
      return header(label).querySelector('button')!;
    }

    function rowNames(): string[] {
      return tableRows().map((row) => row.querySelectorAll('td')[1]?.textContent?.trim() ?? '');
    }

    it('renders a non-sortable column header as plain text -- no button, no aria-sort attribute', () => {
      fixture.detectChanges();

      const idHeader = header('ID');
      expect(idHeader.querySelector('button')).toBeNull();
      expect(idHeader.hasAttribute('aria-sort')).toBe(false);
    });

    it('renders a sortable column header as a real, focusable, non-disabled <button>, initially aria-sort="none"', () => {
      const sortableColumns: ColumnDef<Row>[] = [
        { header: 'ID', cell: (row) => String(row.id) },
        { header: 'Name', cell: (row) => row.name, sortable: true, sortValue: (row) => row.name },
      ];
      fixture.componentRef.setInput('columns', sortableColumns);
      fixture.detectChanges();

      const nameHeader = header('Name');
      expect(nameHeader.getAttribute('aria-sort')).toBe('none');

      const button = sortButton('Name');
      expect(button.tagName).toBe('BUTTON');
      expect(button.getAttribute('type')).toBe('button');
      expect(button.disabled).toBe(false);
      expect(button.tabIndex).not.toBe(-1);
    });

    it('cycles ascending -> descending -> none on repeated activation, re-sorting the current page\'s rows and updating aria-sort each time', () => {
      const sortableColumns: ColumnDef<Row>[] = [
        { header: 'ID', cell: (row) => String(row.id) },
        { header: 'Name', cell: (row) => row.name, sortable: true, sortValue: (row) => row.name },
      ];
      fixture.componentRef.setInput('columns', sortableColumns);
      fixture.componentRef.setInput('rows', [
        { id: 1, name: 'beta' },
        { id: 2, name: 'alpha' },
        { id: 3, name: 'gamma' },
      ]);
      fixture.detectChanges();

      expect(rowNames()).toEqual(['beta', 'alpha', 'gamma']); // as-received order

      sortButton('Name').click();
      fixture.detectChanges();
      expect(header('Name').getAttribute('aria-sort')).toBe('ascending');
      expect(rowNames()).toEqual(['alpha', 'beta', 'gamma']);

      sortButton('Name').click();
      fixture.detectChanges();
      expect(header('Name').getAttribute('aria-sort')).toBe('descending');
      expect(rowNames()).toEqual(['gamma', 'beta', 'alpha']);

      sortButton('Name').click();
      fixture.detectChanges();
      expect(header('Name').getAttribute('aria-sort')).toBe('none');
      expect(rowNames()).toEqual(['beta', 'alpha', 'gamma']); // back to as-received order
    });

    it('activating via click -- the same event a real Enter/Space keypress fires on a focused <button> -- sorts the column', () => {
      const sortableColumns: ColumnDef<Row>[] = [
        { header: 'ID', cell: (row) => String(row.id) },
        { header: 'Name', cell: (row) => row.name, sortable: true, sortValue: (row) => row.name },
      ];
      fixture.componentRef.setInput('columns', sortableColumns);
      fixture.componentRef.setInput('rows', [
        { id: 1, name: 'beta' },
        { id: 2, name: 'alpha' },
      ]);
      fixture.detectChanges();

      const button = sortButton('Name');
      button.focus();
      expect(document.activeElement).toBe(button);

      button.click();
      fixture.detectChanges();

      expect(header('Name').getAttribute('aria-sort')).toBe('ascending');
      expect(rowNames()).toEqual(['alpha', 'beta']);
    });

    it('shows a persistent (not hover-only) visual indicator reflecting the active direction, and a neutral one when inactive', () => {
      const sortableColumns: ColumnDef<Row>[] = [
        { header: 'ID', cell: (row) => String(row.id) },
        { header: 'Name', cell: (row) => row.name, sortable: true, sortValue: (row) => row.name },
      ];
      fixture.componentRef.setInput('columns', sortableColumns);
      fixture.detectChanges();

      expect(sortButton('Name').textContent).toContain('⇅');

      sortButton('Name').click();
      fixture.detectChanges();
      expect(sortButton('Name').textContent).toContain('▲');

      sortButton('Name').click();
      fixture.detectChanges();
      expect(sortButton('Name').textContent).toContain('▼');
    });

    it('sorts by the numeric sortValue rather than the formatted cell() string, avoiding lexicographic ordering', () => {
      const priceColumns: ColumnDef<Row>[] = [
        {
          header: 'Price',
          cell: (row) => `R${row.id}`, // "R1", "R2", "R10" -- wrong order as plain text
          sortable: true,
          sortValue: (row) => row.id,
        },
      ];
      fixture.componentRef.setInput('columns', priceColumns);
      fixture.componentRef.setInput('rows', [
        { id: 2, name: 'b' },
        { id: 10, name: 'j' },
        { id: 1, name: 'a' },
      ]);
      fixture.detectChanges();

      sortButton('Price').click();
      fixture.detectChanges();

      const cells = tableRows().map((row) => row.querySelector('td')?.textContent?.trim());
      expect(cells).toEqual(['R1', 'R2', 'R10']);
    });

    it('falls back to cell(row) as the sort key when sortValue is omitted', () => {
      const columnsNoSortValue: ColumnDef<Row>[] = [
        { header: 'Name', cell: (row) => row.name, sortable: true },
      ];
      fixture.componentRef.setInput('columns', columnsNoSortValue);
      fixture.componentRef.setInput('rows', [
        { id: 1, name: 'beta' },
        { id: 2, name: 'alpha' },
      ]);
      fixture.detectChanges();

      sortButton('Name').click();
      fixture.detectChanges();

      const cells = tableRows().map((row) => row.querySelector('td')?.textContent?.trim());
      expect(cells).toEqual(['alpha', 'beta']);
    });

    it('switches the active sort to a newly clicked column, replacing (not stacking on) the previous one', () => {
      const twoSortableColumns: ColumnDef<Row>[] = [
        { header: 'ID', cell: (row) => String(row.id), sortable: true, sortValue: (row) => row.id },
        { header: 'Name', cell: (row) => row.name, sortable: true, sortValue: (row) => row.name },
      ];
      fixture.componentRef.setInput('columns', twoSortableColumns);
      fixture.componentRef.setInput('rows', [
        { id: 2, name: 'beta' },
        { id: 1, name: 'alpha' },
      ]);
      fixture.detectChanges();

      sortButton('ID').click();
      fixture.detectChanges();
      expect(header('ID').getAttribute('aria-sort')).toBe('ascending');

      sortButton('Name').click();
      fixture.detectChanges();
      expect(header('ID').getAttribute('aria-sort')).toBe('none');
      expect(header('Name').getAttribute('aria-sort')).toBe('ascending');
    });

    it('clears the active sort whenever sortResetToken changes, so server order (newest first) shows again', () => {
      const sortableColumns: ColumnDef<Row>[] = [
        { header: 'ID', cell: (row) => String(row.id) },
        { header: 'Name', cell: (row) => row.name, sortable: true, sortValue: (row) => row.name },
      ];
      fixture.componentRef.setInput('columns', sortableColumns);
      fixture.componentRef.setInput('rows', [
        { id: 1, name: 'beta' },
        { id: 2, name: 'alpha' },
      ]);
      fixture.detectChanges();

      sortButton('Name').click();
      fixture.detectChanges();
      expect(rowNames()).toEqual(['alpha', 'beta']);

      fixture.componentRef.setInput('sortResetToken', 1);
      fixture.detectChanges();

      expect(header('Name').getAttribute('aria-sort')).toBe('none');
      expect(rowNames()).toEqual(['beta', 'alpha']); // as-received order again
    });
  });

  /**
   * A page number past the last page (e.g. the last row of the last page was just
   * deactivated/deleted/cancelled, or a filter was toggled) used to leave an empty table
   * while the footer still showed a count. The table asks its owner to move to the last
   * valid page instead.
   */
  describe('out-of-range page', () => {
    function collectPageChanges(): number[] {
      const emitted: number[] = [];
      component.pageChange.subscribe((page) => emitted.push(page));
      return emitted;
    }

    it('emits pageChange with the last valid page when page is beyond it but rows exist', () => {
      fixture.componentRef.setInput('totalCount', 20);
      fixture.componentRef.setInput('pageSize', 20);
      fixture.componentRef.setInput('page', 3);
      const emitted = collectPageChanges();

      fixture.detectChanges();

      expect(emitted).toEqual([1]);
    });

    it('does not emit while the page is within range', () => {
      fixture.componentRef.setInput('totalCount', 100);
      fixture.componentRef.setInput('page', 5);
      const emitted = collectPageChanges();

      fixture.detectChanges();

      expect(emitted).toEqual([]);
    });

    it('does not emit when there are no rows at all (that is an empty state, not a stale page)', () => {
      fixture.componentRef.setInput('totalCount', 0);
      fixture.componentRef.setInput('page', 3);
      const emitted = collectPageChanges();

      fixture.detectChanges();

      expect(emitted).toEqual([]);
    });

    it('does not emit while loading', () => {
      fixture.componentRef.setInput('totalCount', 20);
      fixture.componentRef.setInput('page', 3);
      fixture.componentRef.setInput('loading', true);
      const emitted = collectPageChanges();

      fixture.detectChanges();

      expect(emitted).toEqual([]);
    });
  });
});
