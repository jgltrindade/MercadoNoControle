import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { of } from 'rxjs';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { ReceiptConfirmationService } from './receipt-confirmation.service';
import { Receipt } from './receipts.service';

registerLocaleData(localePt);

describe('ReceiptConfirmationService', () => {
  let http: HttpTestingController;
  let service: ReceiptConfirmationService;
  const confirm = vi.fn();

  beforeEach(() => {
    confirm.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LOCALE_ID, useValue: 'pt-BR' },
        { provide: ConfirmService, useValue: { confirm } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ReceiptConfirmationService);
  });

  afterEach(() => http.verify());

  const warningsConflict = {
    title: 'Conflito',
    detail: 'A nota possui alertas.',
    warnings: [{ code: 'TOTAL_MISMATCH', message: 'O total não confere.' }],
    possibleDuplicates: [
      { id: 9, storeName: 'Mercado A', number: null, series: null, issuedAt: '2026-11-15T14:30:00', total: 33.89, reason: 'Mesma data e mesmo valor total' },
    ],
  };

  it('confirms directly when there are no warnings', () => {
    let result: Receipt | null | undefined;
    service.confirm(5).subscribe((r) => (result = r));

    const request = http.expectOne('/api/receipts/5/confirm');
    expect(request.request.body).toEqual({ acknowledgeWarnings: false });
    request.flush({ id: 5, status: 'Confirmed' });

    expect(result).toEqual({ id: 5, status: 'Confirmed' });
    expect(confirm).not.toHaveBeenCalled();
  });

  it('shows the warnings and confirms again with acknowledgement when accepted', () => {
    confirm.mockReturnValue(of(true));
    let result: Receipt | null | undefined;
    service.confirm(5).subscribe((r) => (result = r));

    http.expectOne('/api/receipts/5/confirm').flush(warningsConflict, { status: 409, statusText: 'Conflict' });

    const dialog = confirm.mock.calls[0][0];
    expect(dialog.details[0]).toBe('O total não confere.');
    expect(dialog.details[1]).toContain('Possível duplicidade (mesma data e mesmo valor total): compra de 15/11/2026 14:30 em Mercado A');
    expect(dialog.details[1]).toMatch(/R\$\s*33,89/);
    const retry = http.expectOne('/api/receipts/5/confirm');
    expect(retry.request.body).toEqual({ acknowledgeWarnings: true });
    retry.flush({ id: 5, status: 'Confirmed' });
    expect(result).toEqual({ id: 5, status: 'Confirmed' });
  });

  it('emits null when the user declines', () => {
    confirm.mockReturnValue(of(false));
    let result: Receipt | null | undefined;
    service.confirm(5).subscribe((r) => (result = r));

    http.expectOne('/api/receipts/5/confirm').flush(warningsConflict, { status: 409, statusText: 'Conflict' });

    expect(result).toBeNull();
  });

  it('propagates other errors', () => {
    let failed = false;
    service.confirm(5).subscribe({ error: () => (failed = true) });

    http.expectOne('/api/receipts/5/confirm').flush({ title: 'Regra', detail: 'Sem itens.' }, { status: 422, statusText: 'Unprocessable' });

    expect(failed).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
  });
});
