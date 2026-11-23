import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { DEFAULT_CURRENCY_CODE, LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { DateAdapter, MAT_DATE_FORMATS, MAT_DATE_LOCALE, MAT_NATIVE_DATE_FORMATS } from '@angular/material/core';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { PtBrDateAdapter } from '../../core/i18n/date-adapter';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { PagedResult } from '../../shared/models/paging';
import { settle } from '../../testing/settle';
import { ReceiptConfirmationService } from './receipt-confirmation.service';
import { ReceiptDetailsPage } from './receipt-details-page';
import { ReceiptsPage } from './receipts-page';
import { Receipt, ReceiptListItem } from './receipts.service';

registerLocaleData(localePt);

const commonProviders = [
  provideHttpClient(),
  provideHttpClientTesting(),
  provideRouter([]),
  { provide: LOCALE_ID, useValue: 'pt-BR' },
  { provide: DEFAULT_CURRENCY_CODE, useValue: 'BRL' },
  { provide: DateAdapter, useClass: PtBrDateAdapter },
  { provide: MAT_DATE_FORMATS, useValue: MAT_NATIVE_DATE_FORMATS },
  { provide: MAT_DATE_LOCALE, useValue: 'pt-BR' },
];

describe('ReceiptsPage', () => {
  let fixture: ComponentFixture<ReceiptsPage>;
  let http: HttpTestingController;
  const confirm = vi.fn(() => of(true));

  const row: ReceiptListItem = {
    id: 1,
    storeId: 2,
    storeName: 'Mercado A',
    number: '123',
    series: '1',
    issuedAt: '2026-11-15T14:30:00',
    total: 33.89,
    itemCount: 3,
    unidentifiedItemCount: 1,
    status: 'Draft',
    hasImage: true,
    createdAt: '2026-11-23T16:00:00Z',
  };
  const page = (items: ReceiptListItem[]): PagedResult<ReceiptListItem> => ({
    items,
    page: 1,
    pageSize: 20,
    totalCount: items.length,
    totalPages: 1,
  });

  async function setup(storeId?: string) {
    TestBed.configureTestingModule({
      imports: [ReceiptsPage],
      providers: [
        ...commonProviders,
        { provide: ConfirmService, useValue: { confirm } },
        { provide: NotificationService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReceiptsPage);
    if (storeId) {
      fixture.componentRef.setInput('storeId', storeId);
    }
    await settle();
  }

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  const expectList = (): TestRequest => http.expectOne((req) => req.url === '/api/receipts');

  it('lists the newest purchases first and shows status, items and totals', async () => {
    await setup();

    const request = expectList();
    expect(request.request.params.get('sortBy')).toBe('issuedAt');
    expect(request.request.params.get('sortDirection')).toBe('desc');
    request.flush(page([row]));
    await settle();

    const text = element().querySelector('tr[mat-row]')!.textContent!;
    expect(text).toContain('15/11/2026');
    expect(text).toContain('Mercado A');
    expect(text).toContain('Nº 123 / série 1');
    expect(text).toContain('Rascunho');
    expect(text).toMatch(/R\$\s*33,89/);
  });

  it('starts filtered by the store from the query string', async () => {
    await setup('2');

    http.expectOne('/api/stores/2').flush({ id: 2, tradeName: 'Mercado A', city: null, cnpj: null });
    expect(expectList().request.params.get('storeId')).toBe('2');
    await settle();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((fixture.componentInstance as any).storeControl.value).toMatchObject({ id: 2, tradeName: 'Mercado A' });
  });

  it('applies status and period filters', async () => {
    await setup();
    expectList().flush(page([row]));
    await settle();

    fixture.componentInstance.onStatusChange('Confirmed');
    await settle();
    expect(expectList().request.params.get('status')).toBe('Confirmed');

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (fixture.componentInstance as any).range.setValue({ start: new Date(2026, 10, 1), end: new Date(2026, 10, 30) });
    await settle();
    const request = expectList();
    expect(request.request.params.get('from')).toBe('2026-11-01');
    expect(request.request.params.get('to')).toBe('2026-11-30');
  });

  it('shows the empty and error states', async () => {
    await setup();
    expectList().flush(page([]));
    await settle();
    expect(element().textContent).toContain('Nenhuma compra registrada');

    fixture.componentInstance.onStatusChange('Draft');
    await settle();
    expectList().flush(null, { status: 0, statusText: 'Unknown Error' });
    await settle();
    expect(element().textContent).toContain('Não foi possível carregar as compras');
  });

  it('deletes after confirmation', async () => {
    await setup();
    expectList().flush(page([row]));
    await settle();

    fixture.componentInstance.delete(row, new Event('click'));
    await settle();

    expect(confirm).toHaveBeenCalled();
    http.expectOne({ method: 'DELETE', url: '/api/receipts/1' }).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    expectList().flush(page([]));
  });
});

describe('ReceiptDetailsPage', () => {
  let fixture: ComponentFixture<ReceiptDetailsPage>;
  let http: HttpTestingController;
  const confirmReceipt = vi.fn();

  const receipt: Receipt = {
    id: 42,
    storeId: 2,
    storeName: 'Mercado A',
    storeCnpj: '11222333000181',
    number: '123',
    series: '1',
    accessKey: '35261111222333000181650010000123451000012345',
    issuedAt: '2026-11-15T14:30:00',
    productsTotal: 18.48,
    discountTotal: 0,
    additionalCharges: 0,
    total: 20,
    paymentMethod: 'Pix',
    status: 'Draft',
    hasImage: true,
    createdAt: '2026-11-23T16:00:00Z',
    updatedAt: null,
    items: [
      { id: 7, lineNumber: 1, description: 'LEITE INT 1L', productId: 3, productName: 'Leite Integral 1L', quantity: 2, unit: 'UN', unitPrice: 5.49, discount: 0, total: 10.98 },
      { id: 8, lineNumber: 2, description: 'PAO FRANCES', productId: null, productName: null, quantity: 0.5, unit: 'KG', unitPrice: 15, discount: 0, total: 7.5 },
    ],
    warnings: [{ code: 'TOTAL_MISMATCH', message: 'O total da nota não confere.' }],
    possibleDuplicates: [
      { id: 9, storeName: 'Mercado A', number: null, series: null, issuedAt: '2026-11-15T10:00:00', total: 20, reason: 'Mesma data e mesmo valor total' },
    ],
  };

  beforeEach(async () => {
    confirmReceipt.mockReset();
    TestBed.configureTestingModule({
      imports: [ReceiptDetailsPage],
      providers: [
        ...commonProviders,
        { provide: ConfirmService, useValue: { confirm: () => of(true) } },
        { provide: NotificationService, useValue: { success: vi.fn(), error: vi.fn() } },
        { provide: ReceiptConfirmationService, useValue: { confirm: confirmReceipt } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReceiptDetailsPage);
    fixture.componentRef.setInput('id', 42);
    await settle();
  });

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;

  async function load(body: Receipt = receipt) {
    http.expectOne('/api/receipts/42').flush(body);
    await settle();
  }

  it('shows header, warnings, items with identification state and totals', async () => {
    await load();

    const text = element().textContent!;
    expect(text).toContain('Mercado A');
    expect(text).toContain('15/11/2026 às 14:30');
    expect(text).toContain('11.222.333/0001-81');
    expect(text).toContain('3526 1111 2223 3300 0181 6500 1000 0123 4510 0001 2345');
    expect(text).toContain('Rascunho: esta compra ainda não participa dos relatórios');
    expect(text).toContain('O total da nota não confere.');
    expect(text).toContain('Possível duplicidade');
    expect(text).toContain('Leite Integral 1L');
    expect(text).toContain('Produto não identificado');
    expect(text).toContain('1 sem produto associado');
    expect(element().querySelector<HTMLImageElement>('img.receipt-image')!.getAttribute('src')).toBe(
      '/api/receipts/42/image?v=2026-11-23T16%3A00%3A00Z',
    );
  });

  it('confirms a draft and reloads', async () => {
    await load();
    confirmReceipt.mockReturnValue(of({ ...receipt, status: 'Confirmed' }));

    fixture.componentInstance.confirmPurchase();
    await settle();

    expect(confirmReceipt).toHaveBeenCalledWith(42);
    await load({ ...receipt, status: 'Confirmed', warnings: [], possibleDuplicates: [] });
    expect(element().textContent).toContain('Confirmada');
    expect(element().textContent).not.toContain('Confirmar compra');
  });

  it('removes the image after confirmation', async () => {
    await load();

    fixture.componentInstance.removeImage();
    await settle();

    http.expectOne({ method: 'DELETE', url: '/api/receipts/42/image' }).flush({ ...receipt, hasImage: false });
    await settle();
    await load({ ...receipt, hasImage: false });
    expect(element().textContent).toContain('Nenhuma imagem anexada.');
  });

  it('shows a not-found state', async () => {
    http.expectOne('/api/receipts/42').flush({ title: 'Não encontrado' }, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(element().textContent).toContain('Compra não encontrada');
  });
});
