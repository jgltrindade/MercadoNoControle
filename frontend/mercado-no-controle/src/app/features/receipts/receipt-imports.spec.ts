import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DEFAULT_CURRENCY_CODE, LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { Observable, of } from 'rxjs';
import { providePtBrDateAdapter } from '../../core/i18n/date-adapter';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { settle } from '../../testing/settle';
import { StoreFormDialog } from '../supermarkets/store-form-dialog';
import { IMPORT_POLL_INTERVAL, ImportReviewPage } from './import-review-page';
import { ImportsService, ReceiptDraft, ReceiptImport } from './imports.service';
import { ReceiptPickers } from './pickers';
import { ReceiptConfirmationService } from './receipt-confirmation.service';
import { ReceiptFormPage } from './receipt-form-page';
import { MAX_AI_IMAGE_BYTES, ReceiptImportCard } from './receipt-import-card';
import { Receipt } from './receipts.service';

registerLocaleData(localePt);

const draft: ReceiptDraft = {
  storeId: 2,
  storeName: 'Mercado Teste',
  extractedStoreName: 'MERCADO TESTE LTDA',
  extractedCnpj: '11222333000181',
  number: '123456',
  series: '1',
  accessKey: '35261111222333000181650010000123451000012345',
  issuedAt: '2026-11-15T14:30:00',
  productsTotal: 37.53,
  discountTotal: 1,
  additionalCharges: 0,
  total: 36.53,
  paymentMethod: 'Cartao de Debito',
  items: [
    { lineNumber: 1, description: 'BANANA PRATA KG', quantity: 1.235, unit: 'KG', unitPrice: 6.99, discount: 0, total: 8.63, productId: null, productName: null, matchCount: 0 },
    { lineNumber: 2, description: 'ARROZ T1 5KG', quantity: 1, unit: 'UN', unitPrice: 28.9, discount: 1, total: 27.9, productId: 7, productName: 'Arroz 5kg', matchCount: 1 },
    { lineNumber: 3, description: 'LEITE INT', quantity: null, unit: null, unitPrice: null, discount: null, total: null, productId: null, productName: null, matchCount: 2 },
  ],
  warnings: [{ code: 'EXTRACTED_VALUES_MISSING', message: 'Alguns valores não foram lidos.' }],
};

const importFixture = (overrides: Partial<ReceiptImport> = {}): ReceiptImport => ({
  id: 'abc',
  status: 'AwaitingReview',
  originalFileName: 'nota.jpg',
  fileSize: 1000,
  provider: 'Anthropic',
  model: 'claude-opus-5-5',
  error: null,
  attemptCount: 1,
  createdAt: '2026-11-23T16:00:00Z',
  processedAt: '2026-11-23T16:00:30Z',
  receiptId: null,
  draft,
  ...overrides,
});

const baseProviders = [
  provideHttpClient(),
  provideHttpClientTesting(),
  provideRouter([]),
  providePtBrDateAdapter(),
  { provide: LOCALE_ID, useValue: 'pt-BR' },
  { provide: DEFAULT_CURRENCY_CODE, useValue: 'BRL' },
];

describe('ImportsService', () => {
  it('uses the import endpoints and sends the acknowledgement with the reviewed data', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const service = TestBed.inject(ImportsService);
    const file = new File(['x'], 'nota.jpg', { type: 'image/jpeg' });

    service.readerStatus().subscribe();
    service.upload(file).subscribe();
    service.listOpen().subscribe();
    service.get('abc').subscribe();
    service.reprocess('abc').subscribe();
    service.discard('abc').subscribe();
    service.confirm('abc', { storeId: 2 } as never, true).subscribe();

    http.expectOne({ method: 'GET', url: '/api/receipts/imports/reader' });
    expect(((http.expectOne({ method: 'POST', url: '/api/receipts/upload' }).request.body as FormData).get('file') as File).name).toBe('nota.jpg');
    http.expectOne({ method: 'GET', url: '/api/receipts/imports' });
    http.expectOne({ method: 'GET', url: '/api/receipts/imports/abc' });
    http.expectOne({ method: 'POST', url: '/api/receipts/imports/abc/reprocess' });
    http.expectOne({ method: 'PUT', url: '/api/receipts/imports/abc/discard' });
    expect(http.expectOne({ method: 'PUT', url: '/api/receipts/imports/abc/confirm' }).request.body).toEqual({
      storeId: 2,
      acknowledgeWarnings: true,
    });
    expect(service.imageUrl('abc')).toBe('/api/receipts/imports/abc/image');
    http.verify();
  });
});

describe('ReceiptImportCard', () => {
  let fixture: ComponentFixture<ReceiptImportCard>;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [ReceiptImportCard], providers: baseProviders });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ReceiptImportCard);
    await settle();
  });

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const card = () => fixture.componentInstance as any;
  const select = (file: File) => card().onFileSelected({ target: { files: [file], value: 'x' } } as unknown as Event);

  async function respond(available: boolean, open: ReceiptImport[] = []) {
    http.expectOne('/api/receipts/imports/reader').flush({
      available,
      provider: available ? 'Anthropic' : 'Nenhum',
      model: available ? 'claude-opus-5-5' : null,
      message: available ? null : 'A leitura automática não está configurada (chave de API ausente).',
    });
    http.expectOne('/api/receipts/imports').flush(open);
    await settle();
  }

  it('uploads the photo and opens the review page', async () => {
    await respond(true);
    expect(element().querySelector('[data-testid="upload-button"]')).not.toBeNull();

    select(new File(['x'], 'nota.jpg', { type: 'image/jpeg' }));
    http.expectOne('/api/receipts/upload').flush(importFixture({ id: 'novo', status: 'Pending', draft: null }));
    await settle();

    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/receipts/imports', 'novo']);
  });

  it('rejects images above the reading limit and unsupported formats before uploading', async () => {
    await respond(true);

    select(new File([new Uint8Array(MAX_AI_IMAGE_BYTES + 1)], 'grande.jpg', { type: 'image/jpeg' }));
    expect(card().error()).toContain('5 MB');

    select(new File(['x'], 'nota.heic', { type: 'image/heic' }));
    expect(card().error()).toContain('Formato não suportado');
    http.expectNone('/api/receipts/upload');
  });

  it('explains when automatic reading is unavailable and keeps manual registration', async () => {
    await respond(false);

    expect(element().querySelector('[data-testid="upload-button"]')).toBeNull();
    expect(element().textContent).toContain('chave de API ausente');

  });

  it('lists open readings with their status', async () => {
    await respond(true, [importFixture({ draft: null }), importFixture({ id: 'falha', status: 'Failed', draft: null })]);

    const items = Array.from(element().querySelectorAll('.open-list li')).map((li) => li.textContent);
    expect(items[0]).toContain('Aguardando conferência');
    expect(items[1]).toContain('Falha na leitura');
  });
});

describe('ImportReviewPage', () => {
  let fixture: ComponentFixture<ImportReviewPage>;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [ImportReviewPage],
      providers: [
        ...baseProviders,
        { provide: IMPORT_POLL_INTERVAL, useValue: 5 },
        { provide: NotificationService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ImportReviewPage);
    fixture.componentRef.setInput('importId', 'abc');
    await settle();
  });

  afterEach(() => {
    // O formulário embutido consulta o leitor/importações apenas na tela Nova compra; aqui nada deve sobrar.
    http.verify();
  });

  const element = () => fixture.nativeElement as HTMLElement;

  async function respond(body: ReceiptImport) {
    http.expectOne('/api/receipts/imports/abc').flush(body);
    await settle();
  }

  async function nextPoll(body: ReceiptImport) {
    await new Promise((resolve) => setTimeout(resolve, 10));
    await respond(body);
  }

  it('polls while reading and then shows the image beside the pre-filled form', async () => {
    await respond(importFixture({ status: 'Processing', draft: null }));
    expect(element().textContent).toContain('Lendo a nota');
    expect(element().querySelector('img.receipt-photo')!.getAttribute('src')).toBe('/api/receipts/imports/abc/image');

    await nextPoll(importFixture());

    expect(element().querySelector('app-receipt-form-page')).not.toBeNull();
    expect(element().textContent).toContain('Confira cada campo com a imagem da nota');
    expect(element().textContent).toContain('Alguns valores não foram lidos.');
    // Leitura concluída: o polling para.
    await new Promise((resolve) => setTimeout(resolve, 20));
    http.expectNone('/api/receipts/imports/abc');
  });

  it('offers retry, manual registration and discard when the reading fails', async () => {
    await respond(importFixture({ status: 'Failed', error: 'O serviço de leitura está indisponível.', draft: null }));

    expect(element().textContent).toContain('O serviço de leitura está indisponível.');
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const page = fixture.componentInstance as any;

    page.manualEntry.set(true);
    await settle();
    expect(element().querySelector('app-receipt-form-page')).not.toBeNull();

    page.reprocess();
    http.expectOne({ method: 'POST', url: '/api/receipts/imports/abc/reprocess' }).flush(importFixture({ status: 'Pending', draft: null }));
    await settle();
    expect(page.manualEntry()).toBe(false);
    await respond(importFixture({ status: 'Pending', draft: null }));
    await nextPoll(importFixture({ status: 'Failed', error: 'ainda fora do ar', draft: null }));

    page.discard();
    http.expectOne({ method: 'PUT', url: '/api/receipts/imports/abc/discard' }).flush(importFixture({ status: 'Discarded', draft: null }));
    await settle();
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/receipts/new']);
  });

  it('links to the purchase once confirmed', async () => {
    await respond(importFixture({ status: 'Confirmed', receiptId: 42, draft: null }));

    expect(element().textContent).toContain('Compra registrada');
    expect(element().querySelector('a[href="/receipts/42"]')).not.toBeNull();
  });

  it('shows a not-found state', async () => {
    http.expectOne('/api/receipts/imports/abc').flush({ title: 'Não encontrado' }, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(element().textContent).toContain('Leitura não encontrada');
  });
});

describe('ReceiptFormPage in import review mode', () => {
  let fixture: ComponentFixture<ReceiptFormPage>;
  let http: HttpTestingController;
  const confirmWith = vi.fn();
  const createStore = vi.fn();

  beforeEach(async () => {
    confirmWith.mockReset();
    createStore.mockReset();
    TestBed.configureTestingModule({
      imports: [ReceiptFormPage],
      providers: [
        ...baseProviders,
        { provide: NotificationService, useValue: { success: vi.fn(), error: vi.fn() } },
        { provide: ReceiptConfirmationService, useValue: { confirmWith, confirm: vi.fn() } },
      ],
    });
    TestBed.overrideProvider(ReceiptPickers, {
      useValue: { searchStores: () => of([]), searchProducts: () => of([]), createStore, createProduct: vi.fn() },
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ReceiptFormPage);
    fixture.componentRef.setInput('importId', 'abc');
  });

  afterEach(() => http.verify());

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const page = () => fixture.componentInstance as any;
  const form = () => page().form;

  async function open(value: ReceiptDraft | null) {
    fixture.componentRef.setInput('draft', value);
    await settle();
  }

  it('pre-fills the form with what was read, keeping printed totals and unread fields empty', async () => {
    await open(draft);

    expect(form().controls.store.value).toEqual({ id: 2, tradeName: 'Mercado Teste' });
    expect(form().controls.issuedTime.value).toBe('14:30');
    expect(form().controls.accessKey.value).toBe(draft.accessKey);
    expect(form().controls.autoTotals.value).toBe(false);
    expect(form().controls.total.value).toBe(36.53);

    const items = form().controls.items;
    expect(items.length).toBe(3);
    expect(items.at(1).controls.product.value).toEqual({ id: 7, name: 'Arroz 5kg' });
    expect(items.at(1).controls.hint.value).toContain('Associado pela descrição');
    expect(items.at(1).controls.total.value).toBe(27.9);
    expect(items.at(2).controls.hint.value).toContain('2 produtos');
    expect(items.at(2).controls.unitPrice.value).toBeNull();
    expect(form().valid).toBe(false); // valores não lidos precisam ser preenchidos

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('Confirmar compra');
    expect(text).toContain('Descartar leitura');
    expect(text).not.toContain('Imagem da nota');
  });

  it('offers to register the store read from the receipt, pre-filled', async () => {
    await open({ ...draft, storeId: null, storeName: null });
    createStore.mockReturnValue(of({ id: 9, tradeName: 'MERCADO TESTE LTDA' }));

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Cadastrar "MERCADO TESTE LTDA" lido da nota');
    page().createStore();

    expect(createStore).toHaveBeenCalledWith({ tradeName: 'MERCADO TESTE LTDA', cnpj: '11222333000181' });
    expect(form().controls.store.value).toEqual({ id: 9, tradeName: 'MERCADO TESTE LTDA' });
  });

  it('confirms through the import and opens the new purchase', async () => {
    await open({ ...draft, items: draft.items.slice(0, 2) });
    let sent: ((ack: boolean) => Observable<Receipt>) | undefined;
    confirmWith.mockImplementation((send: (ack: boolean) => Observable<Receipt>) => {
      sent = send;
      return of({ id: 50 } as Receipt);
    });

    page().save(true);
    await settle();

    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/receipts', 50]);
    sent!(true).subscribe();
    const request = http.expectOne({ method: 'PUT', url: '/api/receipts/imports/abc/confirm' });
    expect(request.request.body).toMatchObject({
      storeId: 2,
      issuedAt: '2026-11-15T14:30:00',
      total: 36.53,
      acknowledgeWarnings: true,
      items: [{ description: 'BANANA PRATA KG', productId: null }, { description: 'ARROZ T1 5KG', productId: 7, total: 27.9 }],
    });
    request.flush({ id: 50 });
  });

  it('stays on the review when the user declines the warnings', async () => {
    await open({ ...draft, items: draft.items.slice(0, 2) });
    confirmWith.mockReturnValue(of(null));

    page().save(true);
    await settle();

    expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
    expect(page().saving()).toBe(false);
  });

  it('starts with an empty item when there is nothing to pre-fill (manual entry after a failure)', async () => {
    await open(null);

    expect(form().controls.items.length).toBe(1);
    expect(form().controls.issuedDate.value).toBeInstanceOf(Date);
    expect(form().controls.autoTotals.value).toBe(true);
  });

  it('discards the reading', async () => {
    await open(draft);

    page().discardImport();
    http.expectOne({ method: 'PUT', url: '/api/receipts/imports/abc/discard' }).flush({});
    await settle();

    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/receipts/new']);
  });
});

describe('StoreFormDialog prefill', () => {
  it('starts a new store with the values read from the receipt', async () => {
    TestBed.configureTestingModule({
      imports: [StoreFormDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { prefill: { tradeName: 'MERCADO TESTE LTDA', cnpj: '11222333000181' } } },
        { provide: NotificationService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(StoreFormDialog);
    await settle();

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const component = fixture.componentInstance as any;
    expect(component.store).toBeNull();
    expect(component.form.controls.tradeName.value).toBe('MERCADO TESTE LTDA');
    expect(component.form.controls.cnpj.value).toBe('11.222.333/0001-81');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Novo supermercado');
  });
});

describe('ReceiptConfirmationService.confirmWith', () => {
  it('sends without acknowledgement first and retries with it after the user accepts', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCALE_ID, useValue: 'pt-BR' },
        { provide: ConfirmService, useValue: { confirm: () => of(true) } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const service = TestBed.inject(ReceiptConfirmationService);
    const imports = TestBed.inject(ImportsService);
    let result: Receipt | null | undefined;

    service
      .confirmWith((ack) => imports.confirm('abc', { storeId: 2 } as never, ack))
      .subscribe((receipt) => (result = receipt));

    http
      .expectOne('/api/receipts/imports/abc/confirm')
      .flush({ title: 'Conflito', warnings: [{ code: 'X', message: 'alerta' }], possibleDuplicates: [] }, { status: 409, statusText: 'Conflict' });
    const retry = http.expectOne('/api/receipts/imports/abc/confirm');
    expect(retry.request.body.acknowledgeWarnings).toBe(true);
    retry.flush({ id: 50 });

    expect(result).toEqual({ id: 50 });
    http.verify();
  });
});
