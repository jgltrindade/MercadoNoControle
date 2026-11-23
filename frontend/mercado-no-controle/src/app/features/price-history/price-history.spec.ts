import { Component, DEFAULT_CURRENCY_CODE, LOCALE_ID, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { NotificationService } from '../../core/services/notification.service';
import { AppChart } from '../../shared/components/chart/chart';
import { settle } from '../../testing/settle';
import { ReceiptPickers } from '../receipts/pickers';
import { AssignItemsDialog } from './assign-items-dialog';
import {
  CATEGORICAL_PALETTE,
  OTHER_COLOR,
  buildPriceTrendOption,
  buildStoreComparisonOption,
  metricLabel,
  storeColors,
} from './chart-options';
import { PriceHistoryPage, periodQuery } from './price-history-page';
import { PricePoint, PriceSummary, ProductPriceComparison, ProductPriceHistory, UnassignedItemGroup } from './price-history.service';
import { PriceSummaryTiles } from './price-summary-tiles';

registerLocaleData(localePt);

const point = (overrides: Partial<PricePoint>): PricePoint => ({
  observationId: 1,
  receiptId: 1,
  date: '2026-11-01T10:00:00',
  storeId: 1,
  storeName: 'Mercado A',
  quantity: 1,
  unit: 'UN',
  unitPrice: 5,
  totalPrice: 5,
  effectiveUnitPrice: 5,
  normalizedPrice: 5,
  normalizedUnit: 'Liter',
  ...overrides,
});

const summary: PriceSummary = {
  count: 3,
  last: { price: 5.49, date: '2026-11-15T10:00:00', storeName: 'Mercado A' },
  lowest: { price: 4.99, date: '2026-09-10T10:00:00', storeName: 'Mercado A' },
  highest: { price: 5.5, date: '2026-10-05T10:00:00', storeName: 'Mercado B' },
  average: 5.3267,
  variationPercent: 10.02,
  normalizedUnit: 'Liter',
  lastNormalized: 5.49,
  lowestNormalized: 4.99,
  averageNormalized: 5.3267,
};

const history: ProductPriceHistory = {
  productId: 7,
  productName: 'Leite Integral 1L',
  brand: 'Marca',
  unit: 'L',
  packageContent: 1,
  normalizedUnit: 'Liter',
  summary,
  points: [
    point({ observationId: 1, date: '2026-09-10T10:00:00', effectiveUnitPrice: 4.99, normalizedPrice: 4.99 }),
    point({ observationId: 2, date: '2026-10-05T10:00:00', storeId: 2, storeName: 'Mercado B', effectiveUnitPrice: 5.5, normalizedPrice: 5.5 }),
    point({ observationId: 3, date: '2026-11-15T10:00:00', effectiveUnitPrice: 5.49, normalizedPrice: 5.49 }),
  ],
};

const comparison: ProductPriceComparison = {
  productId: 7,
  productName: 'Leite Integral 1L',
  normalizedUnit: 'Liter',
  stores: [
    { storeId: 2, storeName: 'Mercado B', count: 1, last: { price: 5.5, date: '2026-10-05T10:00:00', storeName: 'Mercado B' }, lowest: 5.5, average: 5.5, lastNormalized: 5.5, differenceToCheapestPercent: 0.18 },
    { storeId: 1, storeName: 'Mercado A', count: 2, last: { price: 5.49, date: '2026-11-15T10:00:00', storeName: 'Mercado A' }, lowest: 4.99, average: 5.24, lastNormalized: 5.49, differenceToCheapestPercent: 0 },
  ],
  alternatives: [
    { productId: 8, name: 'Leite Marca B', brand: null, lastNormalized: 4.79, normalizedUnit: 'Liter', date: '2026-11-05T10:00:00', storeName: 'Mercado B' },
  ],
};

const unassigned: UnassignedItemGroup[] = [
  { normalizedDescription: 'PAO FRANCES', description: 'PAO FRANCES', occurrences: 2, lastSeenAt: '2026-11-03T10:00:00', totalSpent: 13.5, stores: ['Mercado A'], suggestedProductId: null, suggestedProductName: null },
];

describe('chart options', () => {
  it('assigns colors by store id in the fixed categorical order, never cycling', () => {
    const colors = storeColors([30, 10, 20, 10, 1, 2, 3, 4, 5, 6]);

    expect(colors.get(1)).toBe(CATEGORICAL_PALETTE[0]);
    expect(colors.get(2)).toBe(CATEGORICAL_PALETTE[1]);
    expect(colors.get(10)).toBe(CATEGORICAL_PALETTE[6]);
    expect(colors.get(20)).toBe(CATEGORICAL_PALETTE[7]);
    expect(colors.get(30)).toBe(OTHER_COLOR);
  });

  it('builds one line per store with legend and direct labels', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const option = buildPriceTrendOption(history.points, 'paid') as any;

    expect(option.series.map((s: { name: string }) => s.name)).toEqual(['Mercado A', 'Mercado B']);
    expect(option.series[0].data.map((d: number[]) => d[1])).toEqual([4.99, 5.49]);
    expect(option.series[0].lineStyle.width).toBe(2);
    expect(option.series[0].symbolSize).toBeGreaterThanOrEqual(8);
    expect(option.series[0].endLabel.show).toBe(true);
    expect(option.legend.show).not.toBe(false);
    expect(option.color).toEqual([CATEGORICAL_PALETTE[0], CATEGORICAL_PALETTE[1]]);
    expect(option.yAxis.axisLabel.formatter(5.5)).toMatch(/R\$\s*5,50/);
  });

  it('hides the legend for a single store and skips points without normalized price', () => {
    const points = [point({ normalizedPrice: null }), point({ observationId: 2, date: '2026-11-02T10:00:00', normalizedPrice: 6 })];

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const option = buildPriceTrendOption(points, 'normalized') as any;

    expect(option.legend.show).toBe(false);
    expect(option.series[0].data.map((d: number[]) => d[1])).toEqual([6]);
    expect(metricLabel('normalized', 'Kilogram')).toBe('Preço por kg');
    expect(metricLabel('paid', 'Kilogram')).toBe('Preço pago por unidade');
  });

  it('orders the comparison bars from the cheapest and labels only the cheapest', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const option = buildStoreComparisonOption(comparison.stores) as any;

    expect(option.yAxis.data).toEqual(['Mercado A', 'Mercado B']);
    expect(option.series[0].barMaxWidth).toBeLessThanOrEqual(24);
    expect(option.series[0].data.map((d: { label: { show: boolean } }) => d.label.show)).toEqual([true, false]);
  });

  it('converts period presets into dates ending today', () => {
    const today = new Date(2026, 10, 23);

    expect(periodQuery('3m', today)).toEqual({ from: '2026-08-23', to: '2026-11-23' });
    expect(periodQuery('12m', today)).toEqual({ from: '2025-11-23', to: '2026-11-23' });
    expect(periodQuery('all', today)).toEqual({});
  });
});

describe('PriceSummaryTiles', () => {
  it('shows last, lowest, highest, average, variation and normalized prices', async () => {
    TestBed.configureTestingModule({ providers: [{ provide: LOCALE_ID, useValue: 'pt-BR' }] });
    const fixture = TestBed.createComponent(PriceSummaryTiles);
    fixture.componentRef.setInput('summary', summary);
    await settle();

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('Último preço pago');
    expect(text).toMatch(/R\$\s*5,49/);
    expect(text).toContain('15/11/2026 · Mercado A');
    expect(text).toMatch(/R\$\s*5,3267/);
    expect(text).toContain('+10,02%');
    expect(text).toContain('aumento desde a 1ª compra');
    expect(text).toMatch(/Preço por L: último R\$\s*5,49/);
  });
});

@Component({ selector: 'app-chart', template: '<div class="chart-stub" [attr.aria-label]="label()"></div>' })
class ChartStub {
  readonly options = input<unknown>();
  readonly label = input<string>('');
  readonly height = input<number>(300);
}

describe('PriceHistoryPage', () => {
  let fixture: ComponentFixture<PriceHistoryPage>;
  let http: HttpTestingController;
  const open = vi.fn();

  async function setup(productId?: string) {
    open.mockReset();
    TestBed.configureTestingModule({
      imports: [PriceHistoryPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: LOCALE_ID, useValue: 'pt-BR' },
        { provide: DEFAULT_CURRENCY_CODE, useValue: 'BRL' },
        { provide: MatDialog, useValue: { open } },
      ],
    });
    TestBed.overrideComponent(PriceHistoryPage, { remove: { imports: [AppChart] }, add: { imports: [ChartStub] } });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(PriceHistoryPage);
    if (productId) {
      fixture.componentRef.setInput('productId', productId);
    }
    await settle();
  }

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const page = () => fixture.componentInstance as any;

  async function loadProduct() {
    http.expectOne('/api/products/7').flush({ id: 7, name: 'Leite Integral 1L', brand: 'Marca', unit: 'L', packageContent: 1 });
    await settle();
    http.expectOne((r) => r.url === '/api/products/7/prices').flush(history);
    http.expectOne((r) => r.url === '/api/products/7/comparison').flush(comparison);
    await settle();
  }

  it('asks for a product and lists items without product', async () => {
    await setup();
    http.expectOne('/api/price-history/unassigned-items').flush(unassigned);
    await settle();

    expect(element().textContent).toContain('Escolha um produto');
    expect(element().textContent).toContain('PAO FRANCES');
    expect(element().textContent).toContain('2 compra(s)');
  });

  it('loads the product from the query string with indicators, chart, comparison and purchases', async () => {
    await setup('7');
    http.expectOne('/api/price-history/unassigned-items').flush([]);
    await loadProduct();

    const text = element().textContent!;
    expect(text).toContain('Leite Integral 1L');
    expect(text).toContain('Preço médio');
    expect(element().querySelector('.chart-stub')!.getAttribute('aria-label')).toContain('Preço pago por unidade de Leite Integral 1L');
    expect(text).toContain('Mais barato');
    expect(text).toContain('Leite Marca B');
    expect(element().querySelectorAll('table[aria-label="Compras do produto"] tr[mat-row]').length).toBe(3);
    expect(text).toContain('Todos os itens das compras confirmadas estão associados a produtos.');
  });

  it('switches the chart to the normalized price and refetches when the period changes', async () => {
    await setup('7');
    http.expectOne('/api/price-history/unassigned-items').flush([]);
    await loadProduct();

    expect(page().canNormalize()).toBe(true);
    page().metric.set('normalized');
    await settle();
    expect(element().querySelector('.chart-stub')!.getAttribute('aria-label')).toContain('Preço por L');

    page().period.set('3m');
    await settle();
    const prices = http.expectOne((r) => r.url === '/api/products/7/prices');
    expect(prices.request.params.has('from')).toBe(true);
    prices.flush(history);
    http.expectOne((r) => r.url === '/api/products/7/comparison').flush(comparison);
    await settle();
  });

  it('shows an empty state when the product has no purchases in the period', async () => {
    await setup('7');
    http.expectOne('/api/price-history/unassigned-items').flush([]);
    http.expectOne('/api/products/7').flush({ id: 7, name: 'Leite Integral 1L', brand: null, unit: 'L', packageContent: 1 });
    await settle();
    http.expectOne((r) => r.url === '/api/products/7/prices').flush({
      ...history,
      points: [],
      summary: { ...summary, count: 0, last: null, lowest: null, highest: null, average: null, variationPercent: null },
    });
    http.expectOne((r) => r.url === '/api/products/7/comparison').flush({ ...comparison, stores: [], alternatives: [] });
    await settle();

    expect(element().textContent).toContain('Nenhuma compra no período');
    expect(element().querySelector('.chart-stub')).toBeNull();
  });

  it('reloads after assigning items', async () => {
    await setup();
    http.expectOne('/api/price-history/unassigned-items').flush(unassigned);
    await settle();
    open.mockReturnValue({ afterClosed: () => of({ updatedItems: 2, productId: 9, productName: 'Pão' }) });

    page().openAssign(unassigned[0]);
    await settle();

    expect(open).toHaveBeenCalledWith(AssignItemsDialog, expect.objectContaining({ data: unassigned[0] }));
    http.expectOne('/api/price-history/unassigned-items').flush([]);
    await settle();
  });
});

describe('AssignItemsDialog', () => {
  it('assigns the group to the chosen product', async () => {
    const close = vi.fn();
    const success = vi.fn();
    TestBed.configureTestingModule({
      imports: [AssignItemsDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCALE_ID, useValue: 'pt-BR' },
        { provide: DEFAULT_CURRENCY_CODE, useValue: 'BRL' },
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: { ...unassigned[0], suggestedProductId: 9, suggestedProductName: 'Pão Francês' } },
        { provide: NotificationService, useValue: { success, error: vi.fn() } },
      ],
    });
    TestBed.overrideProvider(ReceiptPickers, {
      useValue: { searchProducts: () => of([]), createProduct: vi.fn() },
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AssignItemsDialog);
    await settle();

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const dialog = fixture.componentInstance as any;
    expect(dialog.product.value).toEqual({ id: 9, name: 'Pão Francês' });

    dialog.save();
    const request = http.expectOne({ method: 'POST', url: '/api/price-history/assign' });
    expect(request.request.body).toEqual({ description: 'PAO FRANCES', productId: 9 });
    request.flush({ updatedItems: 2, productId: 9, productName: 'Pão Francês' });
    await settle();

    expect(success).toHaveBeenCalledWith('2 item(ns) associado(s) a Pão Francês.');
    expect(close).toHaveBeenCalledWith({ updatedItems: 2, productId: 9, productName: 'Pão Francês' });
    http.verify();
  });
});
