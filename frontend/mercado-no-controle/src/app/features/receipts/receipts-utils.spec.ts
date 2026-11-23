import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FormControl } from '@angular/forms';
import { MAT_DATE_LOCALE } from '@angular/material/core';
import { PtBrDateAdapter, fromIsoDate, toIsoDate } from '../../core/i18n/date-adapter';
import { accessKeyFormat, toControlPath } from '../../shared/forms/form-errors';
import { ReceiptRequest, ReceiptsService, expectedItemTotal, roundMoney } from './receipts.service';

describe('Receipt helpers', () => {
  it('rounds money half away from zero like the backend', () => {
    expect(roundMoney(8.63265)).toBe(8.63);
    expect(roundMoney(2.345)).toBe(2.35);
    expect(roundMoney(1.005)).toBe(1.01);
    expect(roundMoney(-2.345)).toBe(-2.35);
  });

  it('computes the expected item total', () => {
    expect(expectedItemTotal(1.235, 6.99, 0)).toBe(8.63);
    expect(expectedItemTotal(2, 5.49, 0.5)).toBe(10.48);
    expect(expectedItemTotal(0.5, 15, 0)).toBe(7.5);
  });

  it('maps API error paths to form control paths', () => {
    expect(toControlPath('items[2].productId', { productId: 'product' })).toEqual(['items', '2', 'product']);
    expect(toControlPath('storeId', { storeId: 'store' })).toEqual(['store']);
  });

  it('checks only the access key format', () => {
    expect(accessKeyFormat(new FormControl(''))).toBeNull();
    expect(accessKeyFormat(new FormControl('3526 1111 2223 3300 0181 6500 1000 0123 4510 0001 2345'))).toBeNull();
    expect(accessKeyFormat(new FormControl('123'))).toEqual({ accessKeyFormat: true });
    expect(accessKeyFormat(new FormControl('3526#1111222333000181650010000123451000012345'))).toEqual({ accessKeyFormat: true });
  });

  it('parses dates typed in the Brazilian format', () => {
    TestBed.configureTestingModule({ providers: [PtBrDateAdapter, { provide: MAT_DATE_LOCALE, useValue: 'pt-BR' }] });
    const adapter = TestBed.inject(PtBrDateAdapter);

    const date = adapter.parse('05/11/2026')!;
    expect([date.getFullYear(), date.getMonth(), date.getDate()]).toEqual([2026, 10, 5]);
    expect(adapter.isValid(adapter.parse('31/02/2026')!)).toBe(false);
    expect(adapter.parse('')).toBeNull();
  });

  it('converts local dates to and from ISO without time zone shifts', () => {
    expect(toIsoDate(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
    const date = fromIsoDate('2026-11-15T14:30:00');
    expect([date.getFullYear(), date.getMonth(), date.getDate()]).toEqual([2026, 10, 15]);
  });
});

describe('ReceiptsService', () => {
  let http: HttpTestingController;
  let service: ReceiptsService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ReceiptsService);
  });

  afterEach(() => http.verify());

  it('lists with filters and period', () => {
    service
      .list({ page: 1, pageSize: 20, storeId: 3, status: 'Confirmed', from: '2026-11-01', to: '2026-11-30', search: null })
      .subscribe();

    expect(http.expectOne((req) => req.url === '/api/receipts').request.params.toString()).toBe(
      'page=1&pageSize=20&storeId=3&status=Confirmed&from=2026-11-01&to=2026-11-30',
    );
  });

  it('uses the receipt endpoints', () => {
    const request = { storeId: 1, items: [] } as unknown as ReceiptRequest;
    const file = new File(['x'], 'nota.jpg', { type: 'image/jpeg' });

    service.get(4).subscribe();
    service.create(request).subscribe();
    service.update(4, request).subscribe();
    service.confirm(4, true).subscribe();
    service.delete(4).subscribe();
    service.uploadImage(4, file).subscribe();
    service.removeImage(4).subscribe();

    http.expectOne({ method: 'GET', url: '/api/receipts/4' });
    http.expectOne({ method: 'POST', url: '/api/receipts' });
    http.expectOne({ method: 'PUT', url: '/api/receipts/4' });
    expect(http.expectOne({ method: 'PUT', url: '/api/receipts/4/confirm' }).request.body).toEqual({ acknowledgeWarnings: true });
    http.expectOne({ method: 'DELETE', url: '/api/receipts/4' });
    const upload = http.expectOne({ method: 'POST', url: '/api/receipts/4/image' });
    expect(((upload.request.body as FormData).get('file') as File).name).toBe('nota.jpg');
    http.expectOne({ method: 'DELETE', url: '/api/receipts/4/image' });
    expect(service.imageUrl(4, '2026-11-23T10:00:00Z')).toBe('/api/receipts/4/image?v=2026-11-23T10%3A00%3A00Z');
  });
});
