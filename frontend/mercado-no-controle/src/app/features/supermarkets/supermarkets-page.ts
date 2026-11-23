import { CurrencyPipe, DatePipe } from '@angular/common';
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
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, filter, switchMap } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { PageToolbar } from '../../shared/components/page-toolbar/page-toolbar';
import { PreferencesService } from '../../core/services/preferences.service';
import { PAGE_SIZE_OPTIONS, SortDirection } from '../../shared/models/paging';
import { CnpjPipe } from '../../shared/pipes/cnpj.pipe';
import { latestValue } from '../../shared/utils/resource';
import { StoreFormDialog } from './store-form-dialog';
import { BRAZILIAN_STATES, Store, StoreQuery, StoresService } from './stores.service';

@Component({
  selector: 'app-supermarkets-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    ReactiveFormsModule,
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
    RouterLink,
    CnpjPipe,
    EmptyState,
    PageToolbar,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './supermarkets-page.html',
})
export class SupermarketsPage {
  private readonly service = inject(StoresService);
  private readonly dialog = inject(MatDialog);
  private readonly confirmService = inject(ConfirmService);
  private readonly notification = inject(NotificationService);

  protected readonly columns = ['tradeName', 'cnpj', 'city', 'receiptCount', 'totalSpent', 'lastPurchaseAt', 'actions'];
  protected readonly states = BRAZILIAN_STATES;
  protected readonly pageSizeOptions = PAGE_SIZE_OPTIONS;

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly stateFilter = signal('');
  private readonly search = signal('');
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(inject(PreferencesService).preferences().pageSize);
  protected readonly sort = signal<{ active: string; direction: SortDirection }>({ active: 'tradeName', direction: 'asc' });

  protected readonly query = computed<StoreQuery>(() => ({
    page: this.pageIndex() + 1,
    pageSize: this.pageSize(),
    search: this.search(),
    state: this.stateFilter(),
    sortBy: this.sort().active,
    sortDirection: this.sort().direction,
  }));

  protected readonly stores = rxResource({
    params: () => this.query(),
    stream: ({ params }) => this.service.list(params),
  });
  protected readonly result = latestValue(this.stores);
  protected readonly rows = computed(() => this.result()?.items ?? []);
  protected readonly hasFilters = computed(() => !!this.search() || !!this.stateFilter());
  protected readonly loadError = computed(() => {
    const error = this.stores.error();
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

  onStateChange(state: string): void {
    this.stateFilter.set(state);
    this.pageIndex.set(0);
  }

  onSortChange(sort: Sort): void {
    this.sort.set(sort.direction ? { active: sort.active, direction: sort.direction } : { active: 'tradeName', direction: 'asc' });
    this.pageIndex.set(0);
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  clearFilters(): void {
    this.searchControl.setValue('');
    this.search.set('');
    this.onStateChange('');
  }

  openForm(store?: Store): void {
    this.dialog
      .open(StoreFormDialog, { data: store ?? null, width: '640px' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => this.stores.reload());
  }

  delete(store: Store): void {
    this.confirmService
      .confirm({
        title: 'Excluir supermercado',
        message: `Deseja excluir "${store.tradeName}"? Esta ação não pode ser desfeita.`,
        confirmLabel: 'Excluir',
        destructive: true,
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.service.delete(store.id)),
      )
      .subscribe({
        next: () => {
          this.notification.success('Supermercado excluído.');
          this.stores.reload();
        },
        error: (error: unknown) => this.notification.error(toApiError(error).message),
      });
  }
}
