import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, filter, switchMap } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { PageToolbar } from '../../shared/components/page-toolbar/page-toolbar';
import { PreferencesService } from '../../core/services/preferences.service';
import { PAGE_SIZE_OPTIONS, SortDirection } from '../../shared/models/paging';
import { latestValue } from '../../shared/utils/resource';
import { CategoriesService } from '../categories/categories.service';
import { ProductFormDialog } from './product-form-dialog';
import {
  PRODUCT_STATUS_LABELS,
  ProductListItem,
  ProductQuery,
  ProductStatus,
  ProductsService,
  formatPackage,
} from './products.service';

@Component({
  selector: 'app-products-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    MatSortModule,
    MatTableModule,
    MatTooltipModule,
    EmptyState,
    PageToolbar,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './products-page.html',
})
export class ProductsPage {
  private readonly service = inject(ProductsService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly confirmService = inject(ConfirmService);
  private readonly notification = inject(NotificationService);

  protected readonly columns = ['name', 'category', 'package', 'barcode', 'aliasCount', 'status', 'actions'];
  protected readonly pageSizeOptions = PAGE_SIZE_OPTIONS;
  protected readonly statusLabel = (status: ProductStatus) => PRODUCT_STATUS_LABELS[status];
  protected readonly formatPackage = formatPackage;

  private readonly categoriesService = inject(CategoriesService);
  private readonly categoriesResource = rxResource({ stream: () => this.categoriesService.list() });
  protected readonly categories = computed(() => (this.categoriesResource.hasValue() ? this.categoriesResource.value() : []));

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  private readonly search = signal('');
  protected readonly categoryFilter = signal<number | null>(null);
  protected readonly statusFilter = signal<ProductStatus | null>(null);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(inject(PreferencesService).preferences().pageSize);
  protected readonly sort = signal<{ active: string; direction: SortDirection }>({ active: 'name', direction: 'asc' });

  protected readonly query = computed<ProductQuery>(() => ({
    page: this.pageIndex() + 1,
    pageSize: this.pageSize(),
    search: this.search(),
    categoryId: this.categoryFilter(),
    status: this.statusFilter(),
    sortBy: this.sort().active,
    sortDirection: this.sort().direction,
  }));

  protected readonly products = rxResource({
    params: () => this.query(),
    stream: ({ params }) => this.service.list(params),
  });
  protected readonly result = latestValue(this.products);
  protected readonly rows = computed(() => this.result()?.items ?? []);
  protected readonly hasFilters = computed(() => !!this.search() || this.categoryFilter() !== null || this.statusFilter() !== null);
  protected readonly loadError = computed(() => {
    const error = this.products.error();
    return error ? toApiError(error).message : null;
  });

  constructor() {
    this.searchControl.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((term) => {
        this.search.set(term.trim());
        this.pageIndex.set(0);
      });
  }

  onCategoryChange(categoryId: number | null): void {
    this.categoryFilter.set(categoryId);
    this.pageIndex.set(0);
  }

  onStatusChange(status: ProductStatus | null): void {
    this.statusFilter.set(status);
    this.pageIndex.set(0);
  }

  onSortChange(sort: Sort): void {
    this.sort.set(sort.direction ? { active: sort.active, direction: sort.direction } : { active: 'name', direction: 'asc' });
    this.pageIndex.set(0);
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  clearFilters(): void {
    this.searchControl.setValue('');
    this.search.set('');
    this.categoryFilter.set(null);
    this.statusFilter.set(null);
    this.pageIndex.set(0);
  }

  openForm(product?: ProductListItem): void {
    this.dialog
      .open(ProductFormDialog, { data: product ?? null, width: '640px' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe((saved) => {
        if (product) {
          this.products.reload();
        } else {
          void this.router.navigate(['/products', saved.id]);
        }
      });
  }

  delete(product: ProductListItem): void {
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
          this.products.reload();
        },
        error: (error: unknown) => this.notification.error(toApiError(error).message),
      });
  }
}
