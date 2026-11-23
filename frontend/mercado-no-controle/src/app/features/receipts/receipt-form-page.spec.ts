import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { DateAdapter, MAT_DATE_FORMATS, MAT_DATE_LOCALE, MAT_NATIVE_DATE_FORMATS } from '@angular/material/core';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { PtBrDateAdapter } from '../../core/i18n/date-adapter';
import { NotificationService } from '../../core/services/notification.service';
import { settle } from '../../testing/settle';
import { ReceiptConfirmationService } from './receipt-confirmation.service';
import { ReceiptFormPage } from './receipt-form-page';
import { Receipt } from './receipts.service';

registerLocaleData(localePt);

describe('ReceiptFormPage', () => {
  let fixture: ComponentFixture<ReceiptFormPage>;
  let http: HttpTestingController;
  const success = vi.fn();
  const confirmReceipt = vi.fn();

  const savedReceipt: Receipt = {
    id: 42,
    storeId: 2,
    storeName: 'Mercado A',
    storeCnpj: '11222333000181',
    number: '123',
    series: '1',
    accessKey: null,
    issuedAt: '2026-11-15T14:30:00',
    productsTotal: 10.98,
    discountTotal: 0,
    additionalCharges: 0,
    total: 10.98,
    paymentMethod: null,
    status: 'Draft',
    hasImage: false,
    createdAt: '2026-11-23T16:00:00Z',
    updatedAt: null,
    items: [
      { id: 7, lineNumber: 1, description: 'LEITE INT 1L', productId: 3, productName: 'Leite Integral 1L', quantity: 2, unit: 'UN', unitPrice: 5.49, discount: 0, total: 10.98 },
    ],
    warnings: [],
    possibleDuplicates: [],
  };

  async function setup(id?: string) {
    success.mockClear();
    confirmReceipt.mockReset();
    TestBed.configureTestingModule({
      imports: [ReceiptFormPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: LOCALE_ID, useValue: 'pt-BR' },
        { provide: DateAdapter, useClass: PtBrDateAdapter },
        { provide: MAT_DATE_FORMATS, useValue: MAT_NATIVE_DATE_FORMATS },
        { provide: MAT_DATE_LOCALE, useValue: 'pt-BR' },
        { provide: NotificationService, useValue: { success, error: vi.fn() } },
        { provide: ReceiptConfirmationService, useValue: { confirm: confirmReceipt } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ReceiptFormPage);
    if (id) {
      fixture.componentRef.setInput('id', id);
    }
    await settle();
  }

  afterEach(() => {
    // O cartão de leitura por foto (Nova compra) consulta o leitor e as importações em aberto.
    http.match((req) => req.url.startsWith('/api/receipts/imports'));
    http.verify();
  });

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const page = () => fixture.componentInstance as any;
  const form = () => page().form;
  const item = (index: number) => form().controls.items.at(index);

  function fillValidHeader() {
    form().patchValue({ store: { id: 2, tradeName: 'Mercado A' }, issuedDate: new Date(2026, 10, 15), issuedTime: '14:30' });
  }

  describe('new purchase', () => {
    beforeEach(() => setup());

    it('starts with today and one empty item', () => {
      expect(form().controls.items.length).toBe(1);
      expect(form().controls.issuedDate.value).toBeInstanceOf(Date);
      expect(form().controls.autoTotals.value).toBe(true);
    });

    it('calculates item totals and receipt totals from the items', async () => {
      item(0).patchValue({ quantity: 2, unitPrice: 5.49, discount: 0.5 });
      page().addItem();
      item(1).patchValue({ quantity: 0.5, unitPrice: 15 });
      await settle();

      expect(item(0).controls.total.value).toBe(10.48);
      expect(item(1).controls.total.value).toBe(7.5);
      expect(form().controls.productsTotal.value).toBe(18.48);
      expect(form().controls.discountTotal.value).toBe(0.5);
      expect(form().controls.total.value).toBe(17.98);
      expect(page().summary().totalMismatch).toBe(false);
    });

    it('flags a divergent total typed manually', async () => {
      item(0).patchValue({ quantity: 1, unitPrice: 10 });
      form().controls.autoTotals.setValue(false);
      form().controls.total.setValue(12);
      await settle();

      expect(page().summary().totalMismatch).toBe(true);
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('O valor total difere');
    });

    it('suggests the product of a confirmed alias when leaving the description', async () => {
      item(0).controls.description.setValue('leite int 1l');
      page().suggestProduct(item(0));

      http.expectOne((req) => req.url === '/api/products/match' && req.params.get('description') === 'leite int 1l').flush([
        { productId: 3, name: 'Leite Integral 1L', brand: null, unit: 'L', packageContent: 1, status: 'Active', confirmed: true, source: 'Manual' },
      ]);
      await settle();

      expect(item(0).controls.product.value).toMatchObject({ id: 3, name: 'Leite Integral 1L' });
      expect(item(0).controls.hint.value).toContain('Associado automaticamente');
    });

    it('does not pick a product when the description is ambiguous', async () => {
      item(0).controls.description.setValue('LEITE');
      page().suggestProduct(item(0));

      http.expectOne((req) => req.url === '/api/products/match').flush([
        { productId: 3, name: 'Leite A', brand: null, unit: 'L', packageContent: 1, status: 'Active', confirmed: true, source: 'Manual' },
        { productId: 4, name: 'Leite B', brand: null, unit: 'L', packageContent: 1, status: 'Active', confirmed: true, source: 'Manual' },
      ]);
      await settle();

      expect(item(0).controls.product.value).toBeNull();
      expect(item(0).controls.hint.value).toContain('2 produtos');
    });

    it('does not submit an invalid form', async () => {
      page().save(false);
      await settle();

      http.expectNone('/api/receipts');
      expect(page().formError()).toBe('Revise os campos destacados.');
    });

    it('saves a draft with the local date and time, without time zone', async () => {
      fillValidHeader();
      item(0).patchValue({ description: ' LEITE INT 1L ', product: { id: 3, name: 'Leite' }, quantity: 2, unitPrice: 5.49 });
      await settle();

      page().save(false);
      const request = http.expectOne({ method: 'POST', url: '/api/receipts' });
      expect(request.request.body).toMatchObject({
        storeId: 2,
        issuedAt: '2026-11-15T14:30:00',
        productsTotal: 10.98,
        total: 10.98,
        items: [{ id: null, description: 'LEITE INT 1L', productId: 3, quantity: 2, unit: 'UN', unitPrice: 5.49, discount: 0, total: 10.98 }],
      });
      request.flush(savedReceipt);
      await settle();

      expect(success).toHaveBeenCalledWith('Rascunho salvo.');
      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/receipts', 42]);
      expect(confirmReceipt).not.toHaveBeenCalled();
    });

    it('saves and asks for confirmation', async () => {
      confirmReceipt.mockReturnValue(of({ ...savedReceipt, status: 'Confirmed' }));
      fillValidHeader();
      item(0).patchValue({ description: 'ARROZ', quantity: 1, unitPrice: 20 });
      await settle();

      page().save(true);
      http.expectOne('/api/receipts').flush(savedReceipt);
      await settle();

      expect(confirmReceipt).toHaveBeenCalledWith(42);
      expect(success).toHaveBeenCalledWith('Compra confirmada e registrada.');
    });

    it('keeps the purchase as a draft when the user declines the warnings, and updates on retry', async () => {
      confirmReceipt.mockReturnValue(of(null));
      fillValidHeader();
      item(0).patchValue({ description: 'ARROZ', quantity: 1, unitPrice: 20 });
      await settle();

      page().save(true);
      http.expectOne('/api/receipts').flush(savedReceipt);
      await settle();

      expect(success).toHaveBeenCalledWith('Compra salva como rascunho; ela ainda não participa dos relatórios.');
      page().save(false);
      http.expectOne({ method: 'PUT', url: '/api/receipts/42' }).flush(savedReceipt);
      await settle();
    });

    it('shows API errors on the item fields', async () => {
      fillValidHeader();
      item(0).patchValue({ description: 'ARROZ', quantity: 1, unitPrice: 20 });
      await settle();

      page().save(false);
      http.expectOne('/api/receipts').flush(
        { title: 'Itens inválidos', errors: { 'items[0].productId': ['O produto informado não existe.'] } },
        { status: 422, statusText: 'Unprocessable' },
      );
      await settle();

      expect(item(0).controls.product.errors).toEqual({ server: 'O produto informado não existe.' });
    });

    it('shows a duplicate access key on the access key field', async () => {
      fillValidHeader();
      item(0).patchValue({ description: 'ARROZ', quantity: 1, unitPrice: 20 });
      await settle();

      page().save(false);
      http.expectOne('/api/receipts').flush(
        { title: 'Conflito', detail: 'Esta nota já está registrada.', existingReceiptId: 9 },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      expect(form().controls.accessKey.errors).toEqual({ server: 'Esta nota já está registrada.' });
    });

    it('rejects unsupported image files before uploading', () => {
      const event = { target: { files: [new File(['x'], 'nota.pdf', { type: 'application/pdf' })], value: 'x' } } as unknown as Event;

      page().onFileSelected(event);

      expect(page().imageError()).toContain('Formato não suportado');
      expect(page().pendingImage()).toBeNull();
    });

    it('uploads the selected image after saving', async () => {
      const file = new File(['x'], 'nota.jpg', { type: 'image/jpeg' });
      page().onFileSelected({ target: { files: [file], value: 'x' } } as unknown as Event);
      fillValidHeader();
      item(0).patchValue({ description: 'ARROZ', quantity: 1, unitPrice: 20 });
      await settle();

      page().save(false);
      http.expectOne('/api/receipts').flush(savedReceipt);
      await settle();
      http.expectOne({ method: 'POST', url: '/api/receipts/42/image' }).flush({ ...savedReceipt, hasImage: true });
      await settle();

      expect(page().pendingImage()).toBeNull();
      expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/receipts', 42]);
    });
  });

  describe('editing', () => {
    beforeEach(() => setup('42'));

    it('loads the receipt keeping the printed totals and updates it', async () => {
      http.expectOne('/api/receipts/42').flush({ ...savedReceipt, status: 'Confirmed', total: 11 });
      await settle();

      expect(form().controls.autoTotals.value).toBe(false);
      expect(form().controls.total.value).toBe(11);
      expect(form().controls.issuedTime.value).toBe('14:30');
      expect(item(0).controls.id.value).toBe(7);
      expect(item(0).controls.product.value).toMatchObject({ id: 3, name: 'Leite Integral 1L' });
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Esta compra está confirmada');

      page().save(false);
      const request = http.expectOne({ method: 'PUT', url: '/api/receipts/42' });
      expect(request.request.body.items[0].id).toBe(7);
      expect(request.request.body.total).toBe(11);
      request.flush({ ...savedReceipt, status: 'Confirmed' });
      await settle();
      expect(success).toHaveBeenCalledWith('Compra atualizada.');
    });

    it('shows a not-found message', async () => {
      http.expectOne('/api/receipts/42').flush({ title: 'Não encontrado' }, { status: 404, statusText: 'Not Found' });
      await settle();

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Compra não encontrada.');
    });
  });
});
