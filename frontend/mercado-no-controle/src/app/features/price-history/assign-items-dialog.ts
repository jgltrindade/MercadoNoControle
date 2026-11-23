import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { SearchPicker } from '../../shared/components/search-picker/search-picker';
import { ProductRef, ReceiptPickers, productDescription, productName } from '../receipts/pickers';
import { AssignItemsResult, PriceHistoryService, UnassignedItemGroup } from './price-history.service';

/** Associa a um produto todos os itens sem produto com a mesma descrição (RN06). */
@Component({
  selector: 'app-assign-items-dialog',
  imports: [CurrencyPipe, ReactiveFormsModule, MatButtonModule, MatDialogModule, SearchPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Associar a um produto</h2>
    <mat-dialog-content>
      <p>
        <strong>{{ group.description }}</strong><br />
        <span class="muted">
          {{ group.occurrences }} compra(s) · {{ group.totalSpent | currency }} · {{ group.stores.join(', ') }}
        </span>
      </p>
      <app-search-picker
        [formControl]="product"
        label="Produto"
        [required]="true"
        [search]="pickers.searchProducts"
        [display]="productName"
        [describe]="productDescription"
        createLabel="Cadastrar novo produto"
        (create)="createProduct()"
      />
      <p class="muted">
        Todos os itens com esta descrição passam a fazer parte do histórico de preços do produto, e a descrição será
        reconhecida nas próximas compras.
      </p>
      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close [disabled]="saving()">Cancelar</button>
      <button mat-flat-button type="button" [disabled]="saving() || !product.value" (click)="save()">
        {{ saving() ? 'Associando…' : 'Associar' }}
      </button>
    </mat-dialog-actions>
  `,
})
export class AssignItemsDialog {
  private readonly service = inject(PriceHistoryService);
  private readonly notification = inject(NotificationService);
  private readonly dialogRef = inject<MatDialogRef<AssignItemsDialog, AssignItemsResult>>(MatDialogRef);
  protected readonly group = inject<UnassignedItemGroup>(MAT_DIALOG_DATA);
  protected readonly pickers = inject(ReceiptPickers);

  protected readonly productName = productName;
  protected readonly productDescription = productDescription;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly product = new FormControl<ProductRef | null>(
    this.group.suggestedProductId ? { id: this.group.suggestedProductId, name: this.group.suggestedProductName ?? '' } : null,
    Validators.required,
  );

  createProduct(): void {
    this.pickers.createProduct().subscribe((created) => this.product.setValue(created));
  }

  save(): void {
    const product = this.product.value;
    if (!product) {
      this.product.markAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.service.assign(this.group.normalizedDescription, product.id).subscribe({
      next: (result) => {
        this.notification.success(`${result.updatedItems} item(ns) associado(s) a ${result.productName}.`);
        this.dialogRef.close(result);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.error.set(toApiError(error).message);
      },
    });
  }
}
