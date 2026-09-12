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

  it('does not render an actions column when no actions are provided', () => {
    fixture.detectChanges();

    const headers = Array.from(fixture.nativeElement.querySelectorAll('thead th'));
    expect(headers.length).toBe(columns.length);
    expect(tableRows()[0].querySelectorAll('td').length).toBe(columns.length);
  });

  it('renders a trailing actions column and calls the action onClick with the correct row when clicked', () => {
    const onClick = vi.fn();
    const actions: RowAction<Row>[] = [{ label: 'Edit', onClick }];
    fixture.componentRef.setInput('actions', actions);
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
    fixture.componentRef.setInput('actions', actions);
    fixture.detectChanges();

    const secondRowButtons = Array.from<HTMLButtonElement>(
      tableRows()[1].querySelectorAll('td:last-child button'),
    );
    expect(secondRowButtons.map((b) => b.textContent?.trim())).toEqual(['Edit', 'Deactivate']);

    secondRowButtons[1].click();
    expect(deactivateClick).toHaveBeenCalledWith(rows[1]);
    expect(editClick).not.toHaveBeenCalled();
  });
});
