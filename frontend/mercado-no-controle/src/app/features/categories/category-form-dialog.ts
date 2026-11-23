import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { applyServerErrors, emptyToNull, fieldErrorMessage } from '../../shared/forms/form-errors';
import { CategoriesService, Category, CategoryRequest } from './categories.service';

@Component({
  selector: 'app-category-form-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ category ? 'Editar categoria' : 'Nova categoria' }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="form-grid">
        <mat-form-field class="form-grid__full">
          <mat-label>Nome</mat-label>
          <input matInput formControlName="name" maxlength="80" required cdkFocusInitial />
          <mat-error>{{ errorMessage(form.controls.name) }}</mat-error>
        </mat-form-field>
        <mat-form-field class="form-grid__full">
          <mat-label>Descrição</mat-label>
          <textarea matInput formControlName="description" maxlength="250" rows="2"></textarea>
          <mat-error>{{ errorMessage(form.controls.description) }}</mat-error>
        </mat-form-field>
        @if (formError()) {
          <p class="form-error form-grid__full" role="alert">{{ formError() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close [disabled]="saving()">Cancelar</button>
        <button mat-flat-button type="submit" [disabled]="saving()">{{ saving() ? 'Salvando…' : 'Salvar' }}</button>
      </mat-dialog-actions>
    </form>
  `,
})
export class CategoryFormDialog {
  private readonly service = inject(CategoriesService);
  private readonly notification = inject(NotificationService);
  private readonly dialogRef = inject<MatDialogRef<CategoryFormDialog, Category>>(MatDialogRef);
  protected readonly category = inject<Category | null>(MAT_DIALOG_DATA, { optional: true });

  protected readonly errorMessage = fieldErrorMessage;
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: [this.category?.name ?? '', [Validators.required, Validators.maxLength(80)]],
    description: [this.category?.description ?? '', [Validators.maxLength(250)]],
  });

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const request: CategoryRequest = { name: value.name.trim(), description: emptyToNull(value.description) };
    const operation = this.category ? this.service.update(this.category.id, request) : this.service.create(request);

    this.saving.set(true);
    this.formError.set(null);
    operation.subscribe({
      next: (saved) => {
        this.notification.success(this.category ? 'Categoria atualizada.' : 'Categoria criada.');
        this.dialogRef.close(saved);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const apiError = toApiError(error);
        if (apiError.status === 409) {
          this.form.controls.name.setErrors({ server: apiError.message });
        } else if (!applyServerErrors(this.form, apiError)) {
          this.formError.set(apiError.message);
        }
      },
    });
  }
}
