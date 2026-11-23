import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { applyServerErrors, cnpjFormat, emptyToNull, fieldErrorMessage } from '../../shared/forms/form-errors';
import { formatCnpj } from '../../shared/pipes/cnpj.pipe';
import { BRAZILIAN_STATES, Store, StoreRequest, StoresService } from './stores.service';

@Component({
  selector: 'app-store-form-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './store-form-dialog.html',
})
export class StoreFormDialog {
  private readonly service = inject(StoresService);
  private readonly notification = inject(NotificationService);
  private readonly dialogRef = inject<MatDialogRef<StoreFormDialog, Store>>(MatDialogRef);
  protected readonly store = inject<Store | null>(MAT_DIALOG_DATA, { optional: true });

  protected readonly states = BRAZILIAN_STATES;
  protected readonly errorMessage = fieldErrorMessage;
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    tradeName: [this.store?.tradeName ?? '', [Validators.required, Validators.maxLength(150)]],
    legalName: [this.store?.legalName ?? '', [Validators.maxLength(200)]],
    cnpj: [formatCnpj(this.store?.cnpj), [cnpjFormat]],
    address: [this.store?.address ?? '', [Validators.maxLength(250)]],
    city: [this.store?.city ?? '', [Validators.maxLength(100)]],
    state: [this.store?.state ?? ''],
    notes: [this.store?.notes ?? '', [Validators.maxLength(1000)]],
  });

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const request: StoreRequest = {
      tradeName: value.tradeName.trim(),
      legalName: emptyToNull(value.legalName),
      cnpj: emptyToNull(value.cnpj),
      address: emptyToNull(value.address),
      city: emptyToNull(value.city),
      state: emptyToNull(value.state),
      notes: emptyToNull(value.notes),
    };
    const operation = this.store ? this.service.update(this.store.id, request) : this.service.create(request);

    this.saving.set(true);
    this.formError.set(null);
    operation.subscribe({
      next: (saved) => {
        this.notification.success(this.store ? 'Supermercado atualizado.' : 'Supermercado cadastrado.');
        this.dialogRef.close(saved);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const apiError = toApiError(error);
        if (apiError.status === 409) {
          this.form.controls.cnpj.setErrors({ server: apiError.message });
          this.form.controls.cnpj.markAsTouched();
        } else if (!applyServerErrors(this.form, apiError)) {
          this.formError.set(apiError.message);
        }
      },
    });
  }
}
