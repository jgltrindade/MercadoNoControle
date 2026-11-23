import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, numberAttribute, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { filter, switchMap } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { CnpjPipe } from '../../shared/pipes/cnpj.pipe';
import { ReceiptConfirmationService } from './receipt-confirmation.service';
import { RECEIPT_STATUS_LABELS, ReceiptsService } from './receipts.service';

const MAX_IMAGE_BYTES = 10 * 1024 * 1024;
const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

/** Detalhes da nota (rota /receipts/:id). */
@Component({
  selector: 'app-receipt-details-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    DecimalPipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatTableModule,
    MatTooltipModule,
    CnpjPipe,
    EmptyState,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './receipt-details-page.html',
  styleUrl: './receipt-details-page.scss',
})
export class ReceiptDetailsPage {
  private readonly service = inject(ReceiptsService);
  private readonly confirmation = inject(ReceiptConfirmationService);
  private readonly confirmService = inject(ConfirmService);
  private readonly notification = inject(NotificationService);
  private readonly router = inject(Router);

  readonly id = input.required({ transform: numberAttribute });

  protected readonly columns = ['line', 'description', 'quantity', 'unitPrice', 'discount', 'total'];
  protected readonly statusLabels = RECEIPT_STATUS_LABELS;
  protected readonly busy = signal(false);

  protected readonly receiptResource = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.service.get(params),
  });
  protected readonly receipt = computed(() => (this.receiptResource.hasValue() ? this.receiptResource.value() : null));
  protected readonly loadError = computed(() => {
    const error = this.receiptResource.error();
    return error ? toApiError(error) : null;
  });
  protected readonly imageUrl = computed(() => {
    const receipt = this.receipt();
    return receipt?.hasImage ? this.service.imageUrl(receipt.id, receipt.updatedAt ?? receipt.createdAt) : null;
  });
  protected readonly formattedAccessKey = computed(() => this.receipt()?.accessKey?.replace(/(.{4})/g, '$1 ').trim() ?? null);
  protected readonly unidentifiedCount = computed(() => this.receipt()?.items.filter((i) => !i.productId).length ?? 0);

  confirmPurchase(): void {
    this.busy.set(true);
    this.confirmation.confirm(this.id()).subscribe({
      next: (confirmed) => {
        this.busy.set(false);
        if (confirmed) {
          this.notification.success('Compra confirmada e registrada.');
          this.receiptResource.reload();
        }
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.notification.error(toApiError(error).message);
      },
    });
  }

  delete(): void {
    const receipt = this.receipt();
    if (!receipt) return;
    this.confirmService
      .confirm({
        title: 'Excluir compra',
        message:
          receipt.status === 'Confirmed'
            ? 'A compra e seus registros de histórico de preços deixarão de participar dos relatórios. Esta ação não pode ser desfeita.'
            : 'Deseja excluir este rascunho? Esta ação não pode ser desfeita.',
        confirmLabel: 'Excluir',
        destructive: true,
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.service.delete(receipt.id)),
      )
      .subscribe({
        next: () => {
          this.notification.success('Compra excluída.');
          void this.router.navigate(['/receipts']);
        },
        error: (error: unknown) => this.notification.error(toApiError(error).message),
      });
  }

  onImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (!IMAGE_TYPES.includes(file.type)) {
      this.notification.error('Formato não suportado. Envie JPEG, PNG ou WebP.');
      return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      this.notification.error('A imagem excede o tamanho máximo de 10 MB.');
      return;
    }
    this.busy.set(true);
    this.service.uploadImage(this.id(), file).subscribe({
      next: () => {
        this.busy.set(false);
        this.notification.success('Imagem anexada.');
        this.receiptResource.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.notification.error(toApiError(error).message);
      },
    });
  }

  removeImage(): void {
    this.confirmService
      .confirm({
        title: 'Remover imagem',
        message: 'A imagem deixará de ser exibida nesta compra. O arquivo original é preservado no armazenamento.',
        confirmLabel: 'Remover',
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.service.removeImage(this.id())),
      )
      .subscribe({
        next: () => {
          this.notification.success('Imagem removida da compra.');
          this.receiptResource.reload();
        },
        error: (error: unknown) => this.notification.error(toApiError(error).message),
      });
  }
}
