import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import {
  applyServerErrors,
  digitsOnly,
  emptyToNull,
  fieldErrorMessage,
  maxDecimalPlaces,
} from '../../shared/forms/form-errors';
import { CategoriesService } from '../categories/categories.service';
import {
  MEASUREMENT_UNITS,
  MeasurementUnit,
  PRODUCT_STATUS_LABELS,
  Product,
  ProductRequest,
  ProductStatus,
  ProductsService,
} from './products.service';

/** Dados mínimos para editar um produto (vale para item de listagem e para detalhes). */
export type EditableProduct = Pick<
  Product,
  'id' | 'name' | 'brand' | 'categoryId' | 'barcode' | 'unit' | 'packageContent' | 'status'
>;

@Component({
  selector: 'app-product-form-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-form-dialog.html',
})
export class ProductFormDialog {
  private readonly service = inject(ProductsService);
  private readonly categoriesService = inject(CategoriesService);
  private readonly notification = inject(NotificationService);
  private readonly dialogRef = inject<MatDialogRef<ProductFormDialog, Product>>(MatDialogRef);
  protected readonly product = inject<EditableProduct | null>(MAT_DIALOG_DATA, { optional: true });

  protected readonly units = MEASUREMENT_UNITS;
  protected readonly statuses = Object.entries(PRODUCT_STATUS_LABELS) as [ProductStatus, string][];
  protected readonly errorMessage = fieldErrorMessage;
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  private readonly categoriesResource = rxResource({ stream: () => this.categoriesService.list() });
  protected readonly categories = computed(() => (this.categoriesResource.hasValue() ? this.categoriesResource.value() : []));

  protected readonly form = inject(FormBuilder).group({
    name: [this.product?.name ?? '', [Validators.required, Validators.maxLength(150)]],
    brand: [this.product?.brand ?? '', [Validators.maxLength(100)]],
    categoryId: [this.product?.categoryId ?? (null as number | null)],
    barcode: [this.product?.barcode ?? '', [digitsOnly, Validators.maxLength(14)]],
    unit: [this.product?.unit ?? ('UN' as MeasurementUnit), [Validators.required]],
    packageContent: [
      this.product?.packageContent ?? (null as number | null),
      [Validators.min(0.0001), Validators.max(1_000_000), maxDecimalPlaces(4)],
    ],
    status: [this.product?.status ?? ('Active' as ProductStatus), [Validators.required]],
  });

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const request: ProductRequest = {
      name: (value.name ?? '').trim(),
      brand: emptyToNull(value.brand),
      categoryId: value.categoryId ?? null,
      barcode: emptyToNull(value.barcode),
      unit: value.unit!,
      packageContent: value.packageContent ?? null,
      status: value.status!,
    };
    const operation = this.product ? this.service.update(this.product.id, request) : this.service.create(request);

    this.saving.set(true);
    this.formError.set(null);
    operation.subscribe({
      next: (saved) => {
        this.notification.success(this.product ? 'Produto atualizado.' : 'Produto cadastrado.');
        this.dialogRef.close(saved);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const apiError = toApiError(error);
        if (apiError.status === 409) {
          this.form.controls.barcode.setErrors({ server: apiError.message });
          this.form.controls.barcode.markAsTouched();
        } else if (!applyServerErrors(this.form, apiError)) {
          this.formError.set(apiError.message);
        }
      },
    });
  }
}
