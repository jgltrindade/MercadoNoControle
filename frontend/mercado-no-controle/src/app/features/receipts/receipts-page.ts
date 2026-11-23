import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDatepickerModule } from '@angular/material/datepicker';
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
import { toIsoDate } from '../../core/i18n/date-adapter';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { PageToolbar } from '../../shared/components/page-toolbar/page-toolbar';
import { SearchPicker } from '../../shared/components/search-picker/search-picker';
import { PreferencesService } from '../../core/services/preferences.service';
import { PAGE_SIZE_OPTIONS, SortDirection } from '../../shared/models/paging';
import { latestValue } from '../../shared/utils/resource';
import { StoresService } from '../supermarkets/stores.service';
import { ReceiptPickers, StoreRef, storeDescription, storeName, toStoreRef } from './pickers';
import {
  RECEIPT_STATUS_LABELS,
  ReceiptListItem,
  ReceiptQuery,
  ReceiptStatus,
  ReceiptsService,
} from './receipts.service';

@Component({
  selector: 'app-receipts-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatDatepickerModule,
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
    SearchPicker,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './receipts-page.html',
})
export class ReceiptsPage implements OnInit {
  private readonly service = inject(ReceiptsService);
  private readonly storesService = inject(StoresService);
  private readonly router = inject(Router);
  private readonly confirmService = inject(ConfirmService);
  private readonly notification = inject(NotificationService);
  protected readonly pickers = inject(ReceiptPickers);

  /** Filtro inicial por supermercado, vindo da query string (?storeId=). */
  readonly storeId = input<string>();

  protected readonly columns = ['issuedAt', 'store', 'number', 'items', 'total', 'status', 'actions'];
  protected readonly pageSizeOptions = PAGE_SIZE_OPTIONS;
  protected readonly storeName = storeName;
  protected readonly storeDescription = storeDescription;
  protected readonly statusLabel = (status: ReceiptStatus) => RECEIPT_STATUS_LABELS[status];

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly storeControl = new FormControl<StoreRef | null>(null);
  protected readonly range = new FormGroup({
    start: new FormControl<Date | null>(null),
    end: new FormControl<Date | null>(null),
  });

  private readonly search = signal('');
  private readonly storeFilter = signal<number | null>(null);
  protected readonly statusFilter = signal<ReceiptStatus | null>(null);
  private readonly from = signal<string | null>(null);
  private readonly to = signal<string | null>(null);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(inject(PreferencesService).preferences().pageSize);
  protected readonly sort = signal<{ active: string; direction: SortDirection }>({ active: 'issuedAt', direction: 'desc' });

  protected readonly query = computed<ReceiptQuery>(() => ({
    page: this.pageIndex() + 1,
    pageSize: this.pageSize(),
    search: this.search(),
    storeId: this.storeFilter(),
    status: this.statusFilter(),
    from: this.from(),
    to: this.to(),
    sortBy: this.sort().active,
    sortDirection: this.sort().direction,
  }));

  protected readonly receipts = rxResource({
    params: () => this.query(),
    stream: ({ params }) => this.service.list(params),
  });
  protected readonly result = latestValue(this.receipts);
  protected readonly rows = computed(() => this.result()?.items ?? []);
  protected readonly hasFilters = computed(
    () => !!this.search() || this.storeFilter() !== null || this.statusFilter() !== null || !!this.from() || !!this.to(),
  );
  protected readonly loadError = computed(() => {
    const error = this.receipts.error();
    return error ? toApiError(error).message : null;
  });

  constructor() {
    this.searchControl.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((term) => this.applyFilter(() => this.search.set(term.trim())));

    this.storeControl.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((store) => this.applyFilter(() => this.storeFilter.set(store?.id ?? null)));

    this.range.valueChanges.pipe(takeUntilDestroyed()).subscribe(({ start, end }) => {
      // O período só é aplicado quando completo (ou totalmente limpo).
      if ((start && end) || (!start && !end)) {
        this.applyFilter(() => {
          this.from.set(start ? toIsoDate(start) : null);
          this.to.set(end ? toIsoDate(end) : null);
        });
      }
    });
  }

  ngOnInit(): void {
    const storeId = Number(this.storeId());
    if (storeId > 0) {
      this.storeFilter.set(storeId);
      this.storesService.get(storeId).subscribe({
        next: (store) => this.storeControl.setValue(toStoreRef(store), { emitEvent: false }),
        error: () => this.storeFilter.set(null),
      });
    }
  }

  onStatusChange(status: ReceiptStatus | null): void {
    this.applyFilter(() => this.statusFilter.set(status));
  }

  onSortChange(sort: Sort): void {
    this.sort.set(sort.direction ? { active: sort.active, direction: sort.direction } : { active: 'issuedAt', direction: 'desc' });
    this.pageIndex.set(0);
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
  }

  clearFilters(): void {
    this.searchControl.setValue('', { emitEvent: false });
    this.storeControl.setValue(null, { emitEvent: false });
    this.range.setValue({ start: null, end: null }, { emitEvent: false });
    this.search.set('');
    this.storeFilter.set(null);
    this.statusFilter.set(null);
    this.from.set(null);
    this.to.set(null);
    this.pageIndex.set(0);
    void this.router.navigate([], { queryParams: {} });
  }

  open(receipt: ReceiptListItem): void {
    void this.router.navigate(['/receipts', receipt.id]);
  }

  delete(receipt: ReceiptListItem, event: Event): void {
    event.stopPropagation();
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
          this.receipts.reload();
        },
        error: (error: unknown) => this.notification.error(toApiError(error).message),
      });
  }

  private applyFilter(change: () => void): void {
    change();
    this.pageIndex.set(0);
  }
}
