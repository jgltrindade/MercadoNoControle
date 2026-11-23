import { DatePipe } from '@angular/common';
import { Injectable, LOCALE_ID, inject } from '@angular/core';
import { Observable, catchError, of, switchMap, throwError } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { DuplicateCandidate, Receipt, ReceiptWarning, ReceiptsService } from './receipts.service';

/**
 * Confirmação de compra (RN01). Se a API recusar por alertas (409 com `warnings` /
 * `possibleDuplicates`), mostra os alertas ao usuário e só confirma se ele aceitar.
 * Emite a nota confirmada ou `null` se o usuário desistir.
 */
@Injectable({ providedIn: 'root' })
export class ReceiptConfirmationService {
  private readonly receipts = inject(ReceiptsService);
  private readonly confirmService = inject(ConfirmService);
  private readonly datePipe = new DatePipe(inject(LOCALE_ID));

  confirm(receiptId: number): Observable<Receipt | null> {
    return this.receipts.confirm(receiptId, false).pipe(
      catchError((error: unknown) => {
        const apiError = toApiError(error);
        const warnings = (apiError.problem?.['warnings'] as ReceiptWarning[] | undefined) ?? [];
        const duplicates = (apiError.problem?.['possibleDuplicates'] as DuplicateCandidate[] | undefined) ?? [];
        if (apiError.status !== 409 || (warnings.length === 0 && duplicates.length === 0)) {
          return throwError(() => error);
        }

        return this.confirmService
          .confirm({
            title: 'Confirmar compra com alertas?',
            message: 'Revise os alertas abaixo. A compra só será registrada se você confirmar.',
            details: [...warnings.map((w) => w.message), ...duplicates.map((d) => this.describeDuplicate(d))],
            confirmLabel: 'Confirmar mesmo assim',
          })
          .pipe(switchMap((accepted) => (accepted ? this.receipts.confirm(receiptId, true) : of(null))));
      }),
    );
  }

  private describeDuplicate(candidate: DuplicateCandidate): string {
    const date = this.datePipe.transform(candidate.issuedAt, 'dd/MM/yyyy HH:mm');
    const total = candidate.total.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    return `Possível duplicidade (${candidate.reason.toLowerCase()}): compra de ${date} em ${candidate.storeName}, ${total}.`;
  }
}
