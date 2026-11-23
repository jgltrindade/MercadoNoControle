import { Component, DEFAULT_CURRENCY_CODE, LOCALE_ID, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { Router, provideRouter } from '@angular/router';
import { providePtBrDateAdapter } from '../../core/i18n/date-adapter';
import { NotificationService } from '../../core/services/notification.service';
import { CATEGORICAL_PALETTE, OTHER_COLOR, buildColumnOption, buildGroupedBarOption, buildHorizontalBarOption } from '../../shared/charts/chart-theme';
import { AppChart } from '../../shared/components/chart/chart';
import { settle } from '../../testing/settle';
import { ReportsPage, formatCell } from '../reports/reports-page';
import { DEFAULT_FILTERS, filtersFromParams, filtersToParams, presetRange, toAnalyticsQuery } from './analytics-filters';
import { AnalyticsService, DashboardSummary, ReportTable } from './analytics.service';
import { DashboardPage, monthComparison, monthLabel } from './dashboard-page';

registerLocaleData(localePt);

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type AnyOption = any;

describe('chart theme builders', () => {
  it('sorts horizontal bars from largest to smallest and labels only the largest', () => {
    const option: AnyOption = buildHorizontalBarOption(
      [
        { label: 'B', value: 10 },
        { label: 'A', value: 30 },
        { label: 'C', value: 20 },
      ],
      'Total gasto',
    );

    expect(option.yAxis.data).toEqual(['A', 'C', 'B']);
    expect(option.color).toEqual([CATEGORICAL_PALETTE[0]]);
    expect(option.series[0].data.map((d: AnyOption) => d.label.show)).toEqual([true, false, false]);
    expect(option.dataZoom).toBeUndefined();
  });

  it('labels only the latest column and never a dual axis', () => {
    const option: AnyOption = buildColumnOption(['set/26', 'out/26', 'nov/26'], [100, 200, 150], 'Total gasto');

    expect(option.series[0].data.map((d: AnyOption) => d.label.show)).toEqual([false, false, true]);
    expect(Array.isArray(option.yAxis)).toBe(false);
  });

  it('colors grouped series in fixed palette order with a legend', () => {
    const option: AnyOption = buildGroupedBarOption(['Laticínios', 'Padaria'], [
      { name: 'Mês anterior', values: [10, 5] },
      { name: 'Mês atual', values: [12, 0] },
    ]);

    expect(option.color).toEqual([CATEGORICAL_PALETTE[0], CATEGORICAL_PALETTE[1]]);
    expect(option.legend).toBeDefined();
    expect(option.series.map((s: AnyOption) => s.name)).toEqual(['Mês anterior', 'Mês atual']);
    const many: AnyOption = buildGroupedBarOption(['x'], Array.from({ length: 9 }, (_, i) => ({ name: `S${i}`, values: [i] })));
    expect(many.color[8]).toBe(OTHER_COLOR);
  });
});

describe('analytics filters helpers', () => {
  const today = new Date(2026, 10, 23);

  it('computes preset ranges in whole months ending today', () => {
    expect(presetRange('month', today)).toEqual({ from: '2026-11-01', to: '2026-11-23' });
    expect(presetRange('last3', today)).toEqual({ from: '2026-09-01', to: '2026-11-23' });
    expect(presetRange('last12', today)).toEqual({ from: '2025-12-01', to: '2026-11-23' });
    expect(presetRange('year', today)).toEqual({ from: '2026-01-01', to: '2026-11-23' });
    expect(presetRange('lastYear', today)).toEqual({ from: '2025-01-01', to: '2025-12-31' });
  });

  it('builds the API query from the state', () => {
    expect(toAnalyticsQuery({ ...DEFAULT_FILTERS, storeId: 3 }, today)).toEqual({ from: '2025-12-01', to: '2026-11-23', storeId: 3, categoryId: null });
    expect(toAnalyticsQuery({ preset: 'custom', from: '2026-01-10', to: '2026-02-10', storeId: null, categoryId: 4 }, today)).toEqual({
      from: '2026-01-10',
      to: '2026-02-10',
      storeId: null,
      categoryId: 4,
    });
  });

  it('reads and writes the URL state, falling back to defaults on invalid values', () => {
    expect(filtersFromParams({})).toEqual(DEFAULT_FILTERS);
    expect(filtersFromParams({ period: 'nope', storeId: 'abc', categoryId: '-1' })).toEqual(DEFAULT_FILTERS);
    expect(filtersFromParams({ period: 'custom', from: '2026-01-01' }).preset).toBe('last12');
    const custom = filtersFromParams({ period: 'custom', from: '2026-01-01', to: '2026-03-31', storeId: '2', categoryId: '5' });
    expect(custom).toEqual({ preset: 'custom', from: '2026-01-01', to: '2026-03-31', storeId: 2, categoryId: 5 });
    expect(filtersToParams(custom)).toEqual({ period: 'custom', from: '2026-01-01', to: '2026-03-31', storeId: 2, categoryId: 5 });
    expect(filtersToParams({ ...custom, preset: 'year' })).toEqual({ period: 'year', from: null, to: null, storeId: 2, categoryId: 5 });
  });
});

describe('dashboard helpers', () => {
  it('formats month labels in pt-BR', () => {
    expect(monthLabel({ year: 2026, month: 11, total: 0, purchaseCount: 0 })).toBe('nov/26');
  });

  it('aligns previous and current month categories', () => {
    const result = monthComparison(
      [
        { categoryId: 1, categoryName: 'Padaria', total: 30, sharePercent: 50 },
        { categoryId: 2, categoryName: 'Limpeza', total: 30, sharePercent: 50 },
      ],
      [
        { categoryId: 3, categoryName: 'Laticínios', total: 40, sharePercent: 80 },
        { categoryId: 1, categoryName: 'Padaria', total: 10, sharePercent: 20 },
      ],
    );

    expect(result.categories).toEqual(['Laticínios', 'Padaria', 'Limpeza']);
    expect(result.previous).toEqual([0, 30, 30]);
    expect(result.current).toEqual([40, 10, 0]);
  });
});

describe('AnalyticsService', () => {
  let service: AnalyticsService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AnalyticsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends filters as query params and skips empty ones', () => {
    service.summary({ from: '2026-01-01', to: '2026-01-31', storeId: null, categoryId: 2 }).subscribe();

    const request = http.expectOne((r) => r.url === '/api/dashboard');
    expect(request.request.params.get('from')).toBe('2026-01-01');
    expect(request.request.params.get('categoryId')).toBe('2');
    expect(request.request.params.has('storeId')).toBe(false);
    request.flush({});
  });

  it('downloads exports with the file name from Content-Disposition', () => {
    let fileName = '';
    service.export('monthly', 'csv', { from: '2026-01-01', to: '2026-01-31' }).subscribe((file) => (fileName = file.fileName));

    const request = http.expectOne((r) => r.url === '/api/reports/export');
    expect(request.request.params.get('report')).toBe('monthly');
    expect(request.request.params.get('format')).toBe('csv');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['a;b']), {
      headers: { 'Content-Disposition': "attachment; filename=x.csv; filename*=UTF-8''mercado-no-controle-monthly-20261123.csv" },
    });
    expect(fileName).toBe('mercado-no-controle-monthly-20261123.csv');
  });

  it('falls back to a default file name', () => {
    let fileName = '';
    service.export('stores', 'pdf', {}).subscribe((file) => (fileName = file.fileName));
    http.expectOne((r) => r.url === '/api/reports/export').flush(new Blob(['%PDF']));
    expect(fileName).toBe('relatorio-stores.pdf');
  });
});

describe('formatCell', () => {
  it('formats values by column type in pt-BR', () => {
    expect(formatCell(1234.5, 'Money')).toMatch(/R\$\s*1\.234,50/);
    expect(formatCell(12.345, 'Percent')).toBe('12,35%');
    expect(formatCell(1.5, 'Number')).toBe('1,5');
    expect(formatCell(3, 'Integer')).toBe('3');
    expect(formatCell('2026-11-23T00:00:00', 'Date')).toBe('23/11/2026');
    expect(formatCell(null, 'Money')).toBe('—');
    expect(formatCell('Mercado A', 'Text')).toBe('Mercado A');
  });
});

@Component({ selector: 'app-chart', template: '<div class="chart-stub" [attr.aria-label]="label()"></div>' })
class ChartStub {
  readonly options = input<unknown>();
  readonly label = input<string>('');
  readonly height = input<number>(300);
}

const providers = () => [
  provideHttpClient(),
  provideHttpClientTesting(),
  provideRouter([]),
  providePtBrDateAdapter(),
  { provide: LOCALE_ID, useValue: 'pt-BR' },
  { provide: DEFAULT_CURRENCY_CODE, useValue: 'BRL' },
];

const summary: DashboardSummary = {
  from: '2025-12-01',
  to: '2026-11-23',
  totalSpent: 1234.56,
  purchaseCount: 3,
  averageTicket: 411.52,
  largestPurchase: { receiptId: 9, total: 600, date: '2026-11-10T10:00:00', storeName: 'Mercado A' },
  topStore: { storeId: 1, storeName: 'Mercado A', total: 900 },
  currentMonthTotal: 600,
  currentYearTotal: 1234.56,
};

describe('DashboardPage', () => {
  let fixture: ComponentFixture<DashboardPage>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.fn>;

  async function setup(params: Record<string, string> = {}) {
    TestBed.configureTestingModule({ imports: [DashboardPage], providers: providers() });
    TestBed.overrideComponent(DashboardPage, { remove: { imports: [AppChart] }, add: { imports: [ChartStub] } });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as unknown as ReturnType<typeof vi.fn>;
    fixture = TestBed.createComponent(DashboardPage);
    for (const [key, value] of Object.entries(params)) {
      fixture.componentRef.setInput(key, value);
    }
    await settle();
  }

  function flushAll(data: DashboardSummary) {
    http.match((r) => r.url === '/api/categories').forEach((r) => r.flush([{ id: 2, name: 'Laticínios' }]));
    http.expectOne((r) => r.url === '/api/dashboard').flush(data);
    http.expectOne((r) => r.url === '/api/dashboard/monthly').flush([
      { year: 2026, month: 10, total: 634.56, purchaseCount: 2 },
      { year: 2026, month: 11, total: 600, purchaseCount: 1 },
    ]);
    http.expectOne((r) => r.url === '/api/dashboard/stores').flush([{ storeId: 1, storeName: 'Mercado A', total: 900, purchaseCount: 2, sharePercent: 72.9 }]);
    const categories = http.match((r) => r.url === '/api/dashboard/categories');
    expect(categories.length).toBe(3); // período + mês anterior + mês atual
    categories.forEach((r) => r.flush([{ categoryId: 2, categoryName: 'Laticínios', total: 100, sharePercent: 100 }]));
    http.expectOne((r) => r.url === '/api/dashboard/products').flush([]);
  }

  afterEach(() => http.verify());

  const text = () => (fixture.nativeElement as HTMLElement).textContent!;

  it('shows KPIs, charts and report links for the filtered period', async () => {
    await setup({ period: 'year', storeId: '1' });
    http.expectOne('/api/stores/1').flush({ id: 1, tradeName: 'Mercado A', cnpj: null, city: null, state: null });
    const summaryRequest = http.match((r) => r.url === '/api/dashboard')[0];
    expect(summaryRequest.request.params.get('storeId')).toBe('1');
    expect(summaryRequest.request.params.get('from')).toBe(`${new Date().getFullYear()}-01-01`);
    summaryRequest.flush(summary);
    http.expectOne((r) => r.url === '/api/dashboard/monthly').flush([{ year: 2026, month: 11, total: 600, purchaseCount: 1 }]);
    http.expectOne((r) => r.url === '/api/dashboard/stores').flush([]);
    http.match((r) => r.url === '/api/dashboard/categories').forEach((r) => r.flush([]));
    http.expectOne((r) => r.url === '/api/dashboard/products').flush([]);
    http.match((r) => r.url === '/api/categories').forEach((r) => r.flush([]));
    await settle();

    expect(text()).toContain('Total gasto no período');
    expect(text()).toMatch(/R\$\s*1\.234,56/);
    expect(text()).toContain('Mercado A');
    expect(text()).toContain('Ticket médio');
    expect(text()).toContain('Total no ano');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.chart-stub').length).toBeGreaterThan(0);
    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href*="report=monthly"]')!.getAttribute('href')!;
    expect(link).toContain('period=year');
    expect(link).toContain('storeId=1');
    expect(text()).toContain('Nenhum item associado a produtos no período.');
  });

  it('shows the empty state without confirmed purchases', async () => {
    await setup();
    flushAll({ ...summary, totalSpent: 0, purchaseCount: 0, averageTicket: null, largestPurchase: null, topStore: null });
    await settle();

    expect(text()).toContain('Nenhuma compra confirmada no período');
    expect((fixture.nativeElement as HTMLElement).querySelector('.chart-stub')).toBeNull();
  });

  it('syncs filter changes to the URL and refetches', async () => {
    await setup();
    flushAll(summary);
    await settle();

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (fixture.componentInstance as any).onFiltersChanged({ ...DEFAULT_FILTERS, categoryId: 2 });
    await settle();

    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: expect.objectContaining({ categoryId: 2, period: 'last12' }) }));
    const refetch = http.expectOne((r) => r.url === '/api/dashboard');
    expect(refetch.request.params.get('categoryId')).toBe('2');
    refetch.flush(summary);
    http.expectOne((r) => r.url === '/api/dashboard/monthly').flush([]);
    http.expectOne((r) => r.url === '/api/dashboard/stores').flush([]);
    http.match((r) => r.url === '/api/dashboard/categories').forEach((r) => r.flush([]));
    http.expectOne((r) => r.url === '/api/dashboard/products').flush([]);
    await settle();
  });

  it('shows an error with retry when the dashboard fails', async () => {
    await setup();
    http.match((r) => r.url === '/api/categories').forEach((r) => r.flush([]));
    http.expectOne((r) => r.url === '/api/dashboard').flush({ title: 'Período inválido' }, { status: 422, statusText: 'Unprocessable' });
    http.match((r) => r.url.startsWith('/api/dashboard/')).forEach((r) => r.flush([]));
    await settle();

    expect(text()).toContain('Não foi possível carregar o dashboard');
    expect(text()).toContain('Tentar novamente');
  });
});

const monthlyReport: ReportTable = {
  key: 'monthly',
  title: 'Gastos mensais e anuais',
  filters: 'Período: 01/12/2025 a 23/11/2026',
  notes: 'Somente compras confirmadas.',
  generatedAt: '2026-11-23T12:00:00Z',
  columns: [
    { key: 'month', header: 'Mês', type: 'Text' },
    { key: 'purchases', header: 'Compras', type: 'Integer' },
    { key: 'total', header: 'Total', type: 'Money' },
  ],
  rows: [
    { cells: ['out/2026', 2, 634.56], isTotal: false },
    { cells: ['nov/2026', 1, 600], isTotal: false },
    { cells: ['Total do período', 3, 1234.56], isTotal: true },
  ],
};

describe('ReportsPage', () => {
  let fixture: ComponentFixture<ReportsPage>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.fn>;
  const notify = { error: vi.fn(), success: vi.fn() };

  async function setup(params: Record<string, string> = {}) {
    notify.error.mockReset();
    TestBed.configureTestingModule({
      imports: [ReportsPage],
      providers: [...providers(), { provide: NotificationService, useValue: notify }],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true) as unknown as ReturnType<typeof vi.fn>;
    fixture = TestBed.createComponent(ReportsPage);
    for (const [key, value] of Object.entries(params)) {
      fixture.componentRef.setInput(key, value);
    }
    await settle();
    http.match((r) => r.url === '/api/categories').forEach((r) => r.flush([]));
  }

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const page = () => fixture.componentInstance as any;

  it('renders the report table with formatted cells and a bold total row', async () => {
    await setup();
    http.expectOne((r) => r.url === '/api/reports/monthly').flush(monthlyReport);
    await settle();

    expect(element().textContent).toContain('Gastos mensais e anuais');
    expect(element().textContent).toContain('Período: 01/12/2025 a 23/11/2026');
    expect(element().textContent).toContain('Somente compras confirmadas.');
    const rows = element().querySelectorAll('tr[mat-row]');
    expect(rows.length).toBe(3);
    expect(rows[2].classList).toContain('total-row');
    expect(rows[2].textContent).toMatch(/R\$\s*1\.234,56/);
    expect(element().querySelector('th.cell-number')!.textContent).toContain('Compras');
  });

  it('opens the report and ranking from the URL and syncs changes back', async () => {
    await setup({ report: 'products', ranking: 'increase', period: 'month' });
    const request = http.expectOne((r) => r.url === '/api/reports/products');
    expect(request.request.params.get('ranking')).toBe('increase');
    request.flush({ ...monthlyReport, key: 'products', rows: [] });
    await settle();

    expect(element().textContent).toContain('Nenhum dado no período');

    page().selectReport('stores');
    await settle();
    const stores = http.expectOne((r) => r.url === '/api/reports/stores');
    expect(stores.request.params.has('ranking')).toBe(false);
    stores.flush({ ...monthlyReport, key: 'stores' });
    await settle();
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: expect.objectContaining({ report: 'stores', ranking: null, period: 'month' }) }),
    );
  });

  it('exports the displayed report with the same filters', async () => {
    const createObjectURL = vi.fn(() => 'blob:x');
    const revokeObjectURL = vi.fn();
    Object.assign(URL, { createObjectURL, revokeObjectURL });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    await setup({ report: 'categories', categoryId: '4' });
    http.expectOne((r) => r.url === '/api/reports/categories').flush({ ...monthlyReport, key: 'categories' });
    await settle();

    page().download('xlsx');
    const exportRequest = http.expectOne((r) => r.url === '/api/reports/export');
    expect(exportRequest.request.params.get('report')).toBe('categories');
    expect(exportRequest.request.params.get('format')).toBe('xlsx');
    expect(exportRequest.request.params.get('categoryId')).toBe('4');
    exportRequest.flush(new Blob(['x']), { headers: { 'Content-Disposition': 'attachment; filename="relatorio.xlsx"' } });

    expect(createObjectURL).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    click.mockRestore();
  });

  it('shows an error message when the export fails', async () => {
    await setup();
    http.expectOne((r) => r.url === '/api/reports/monthly').flush(monthlyReport);
    await settle();

    page().download('pdf');
    http
      .expectOne((r) => r.url === '/api/reports/export')
      .flush(new Blob([JSON.stringify({ title: 'Erro' })], { type: 'application/problem+json' }), { status: 500, statusText: 'Error' });
    await settle();

    expect(notify.error).toHaveBeenCalled();
    expect(page().exporting()).toBeNull();
  });
});
