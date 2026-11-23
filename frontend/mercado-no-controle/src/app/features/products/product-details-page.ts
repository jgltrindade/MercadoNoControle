import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, numberAttribute, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { filter, switchMap } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { fieldErrorMessage } from '../../shared/forms/form-errors';
import { ProductFormDialog } from './product-form-dialog';
import {
  ALIAS_SOURCE_LABELS,
  PRODUCT_STATUS_LABELS,
  ProductAlias,
  ProductsService,
  formatPackage,
} from './products.service';

@Component({
  selector: 'app-product-details-page',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatListModule,
    MatProgressBarModule,
    MatTooltipModule,
    EmptyState,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-details-page.html',
  styleUrl: './product-details-page.scss',
})
export class ProductDetailsPage {
  private readonly service = inject(ProductsService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly confirmService = inject(ConfirmService);
  private readonly notification = inject(NotificationService);

  /** Parâmetro de rota `:id` (component input binding). */
  readonly id = input.required({ transform: numberAttribute });

  protected readonly statusLabels = PRODUCT_STATUS_LABELS;
  protected readonly sourceLabels = ALIAS_SOURCE_LABELS;
  protected readonly errorMessage = fieldErrorMessage;

  protected readonly productResource = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.service.get(params),
  });
  protected readonly product = computed(() => (this.productResource.hasValue() ? this.productResource.value() : null));
  protected readonly packageLabel = computed(() => {
    const product = this.product();
    return product ? formatPackage(product.packageContent, product.unit) : '';
  });
  protected readonly loadError = computed(() => {
    const error = this.productResource.error();
    return error ? toApiError(error) : null;
  });

  protected readonly aliasForm = new FormGroup({
    description: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(250)] }),
  });
  protected readonly aliasControl = this.aliasForm.controls.description;
  protected readonly addingAlias = signal(false);

  edit(): void {
    const product = this.product();
    if (!product) return;
    this.dialog
      .open(ProductFormDialog, { data: product, width: '640px' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => this.productResource.reload());
  }

  delete(): void {
    const product = this.product();
    if (!product) return;
    this.confirmService
      .confirm({
        title: 'Excluir produto',
        message: `Deseja excluir "${product.name}" e suas descrições alternativas?`,
        confirmLabel: 'Excluir',
        destructive: true,
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.service.delete(product.id)),
      )
      .subscribe({
        next: () => {
          this.notification.success('Produto excluído.');
          void this.router.navigate(['/products']);
        },
        error: (error: unknown) => this.notification.error(toApiError(error).message),
      });
  }

  addAlias(): void {
    if (this.aliasControl.invalid) {
      this.aliasControl.markAsTouched();
      return;
    }
    this.addingAlias.set(true);
    this.service.addAlias(this.id(), this.aliasControl.value.trim()).subscribe({
      next: () => {
        this.addingAlias.set(false);
        this.aliasControl.reset();
        this.notification.success('Descrição associada ao produto.');
        this.productResource.reload();
      },
      error: (error: unknown) => {
        this.addingAlias.set(false);
        const apiError = toApiError(error);
        this.aliasControl.setErrors({ server: apiError.fieldErrors['description']?.join(' ') ?? apiError.message });
        this.aliasControl.markAsTouched();
      },
    });
  }

  confirmAlias(alias: ProductAlias): void {
    this.service.confirmAlias(this.id(), alias.id).subscribe({
      next: () => {
        this.notification.success('Associação confirmada.');
        this.productResource.reload();
      },
      error: (error: unknown) => this.notification.error(toApiError(error).message),
    });
  }

  removeAlias(alias: ProductAlias): void {
    this.confirmService
      .confirm({
        title: 'Remover descrição',
        message: `Remover a associação da descrição "${alias.description}"?`,
        confirmLabel: 'Remover',
        destructive: true,
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.service.deleteAlias(this.id(), alias.id)),
      )
      .subscribe({
        next: () => {
          this.notification.success('Descrição removida.');
          this.productResource.reload();
        },
        error: (error: unknown) => this.notification.error(toApiError(error).message),
      });
  }
}
