import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { toIsoDate } from '../../core/i18n/date-adapter';
import { AppChart } from '../../shared/components/chart/chart';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { PageToolbar } from '../../shared/components/page-toolbar/page-toolbar';
import { SearchPicker } from '../../shared/components/search-picker/search-picker';
import { ProductsService, formatPackage } from '../products/products.service';
import { ProductRef, ReceiptPickers, productDescription, productName } from '../receipts/pickers';
import { AssignItemsDialog } from './assign-items-dialog';
import { PriceMetric, buildPriceTrendOption, buildStoreComparisonOption, metricLabel } from './chart-options';
import {
  NORMALIZED_UNIT_LABELS,
  NormalizedUnit,
  PriceHistoryQuery,
  PriceHistoryService,
  UnassignedItemGroup,
} from './price-history.service';
import { PriceSummaryTiles } from './price-summary-tiles';

export type PeriodPreset = '3m' | '6m' | '12m' | 'all';

export const PERIOD_OPTIONS: readonly { value: PeriodPreset; label: string }[] = [
  { value: '3m', label: 'Últimos 3 meses' },
  { value: '6m', label: 'Últimos 6 meses' },
  { value: '12m', label: 'Últimos 12 meses' },
  { value: 'all', label: 'Todo o período' },
];

/** Converte o período escolhido em datas (a data final é hoje). */
export function periodQuery(preset: PeriodPreset, today = new Date()): PriceHistoryQuery {
  if (preset === 'all') {
    return {};
  }
  const months = { '3m': 3, '6m': 6, '12m': 12 }[preset];
  const from = new Date(today.getFullYear(), today.getMonth() - months, today.getDate());
  return { from: toIsoDate(from), to: toIsoDate(today) };
}

/** Histórico de preços (rota /price-history, com `?productId=` opcional). */
@Component({
  selector: 'app-price-history-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    DecimalPipe,
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTableModule,
    AppChart,
    EmptyState,
    PageToolbar,
    PriceSummaryTiles,
    SearchPicker,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './price-history-page.html',
  styleUrl: './price-history-page.scss',
})
export class PriceHistoryPage implements OnInit {
  private readonly service = inject(PriceHistoryService);
  private readonly productsService = inject(ProductsService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  protected readonly pickers = inject(ReceiptPickers);

  /** Produto inicial vindo da query string. */
  readonly productId = input<string>();

  protected readonly periodOptions = PERIOD_OPTIONS;
  protected readonly productName = productName;
  protected readonly productDescription = productDescription;
  protected readonly unitLabels = NORMALIZED_UNIT_LABELS;
  protected readonly unitLabel = (unit: NormalizedUnit | null) => (unit ? NORMALIZED_UNIT_LABELS[unit] : '');
  protected readonly pointColumns = ['date', 'store', 'quantity', 'paid', 'normalized'];
  protected readonly storeColumns = ['store', 'last', 'lowest', 'average', 'difference'];

  protected readonly productControl = new FormControl<ProductRef | null>(null);
  protected readonly selectedProduct = signal<ProductRef | null>(null);
  protected readonly period = signal<PeriodPreset>('all');
  protected readonly metric = signal<PriceMetric>('paid');

  private readonly params = computed(() => {
    const product = this.selectedProduct();
    return product ? { id: product.id, query: periodQuery(this.period()) } : undefined;
  });

  protected readonly history = rxResource({
    params: () => this.params(),
    stream: ({ params }) => this.service.getPrices(params.id, params.query),
  });
  protected readonly comparison = rxResource({
    params: () => this.params(),
    stream: ({ params }) => this.service.getComparison(params.id, params.query),
  });
  protected readonly unassigned = rxResource({ stream: () => this.service.unassignedItems() });

  protected readonly data = computed(() => (this.history.hasValue() ? this.history.value() : null));
  protected readonly comparisonData = computed(() => (this.comparison.hasValue() ? this.comparison.value() : null));
  protected readonly unassignedGroups = computed(() => (this.unassigned.hasValue() ? this.unassigned.value() : []));
  protected readonly loadError = computed(() => {
    const error = this.history.error() ?? this.comparison.error();
    return error ? toApiError(error) : null;
  });

  /** O preço normalizado só é oferecido quando todas as compras do período o têm. */
  protected readonly canNormalize = computed(() => {
    const data = this.data();
    return !!data && data.points.length > 0 && data.points.every((p) => p.normalizedPrice !== null);
  });
  protected readonly effectiveMetric = computed<PriceMetric>(() => (this.canNormalize() ? this.metric() : 'paid'));
  protected readonly trendOption = computed(() => buildPriceTrendOption(this.data()?.points ?? [], this.effectiveMetric()));
  protected readonly comparisonOption = computed(() => buildStoreComparisonOption(this.comparisonData()?.stores ?? []));
  protected readonly trendLabel = computed(() => {
    const data = this.data();
    if (!data) return '';
    const label = metricLabel(this.effectiveMetric(), data.normalizedUnit);
    return `${label} de ${data.productName} ao longo do tempo, por supermercado. Os valores estão na tabela "Compras".`;
  });
  protected readonly packageLabel = computed(() => {
    const data = this.data();
    return data ? formatPackage(data.packageContent, data.unit) : '';
  });

  constructor() {
    this.productControl.valueChanges.pipe(takeUntilDestroyed()).subscribe((product) => this.select(product));
  }

  ngOnInit(): void {
    const id = Number(this.productId());
    if (id > 0) {
      this.productsService.get(id).subscribe({
        next: (product) => {
          const ref = { id: product.id, name: product.name, brand: product.brand, detail: formatPackage(product.packageContent, product.unit) };
          this.productControl.setValue(ref, { emitEvent: false });
          this.selectedProduct.set(ref);
        },
        error: () => this.selectedProduct.set(null),
      });
    }
  }

  openAssign(group: UnassignedItemGroup): void {
    this.dialog
      .open(AssignItemsDialog, { data: group, width: '560px' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe((result) => {
        this.unassigned.reload();
        if (this.selectedProduct()?.id === result.productId) {
          this.history.reload();
          this.comparison.reload();
        }
      });
  }

  private select(product: ProductRef | null): void {
    this.selectedProduct.set(product);
    this.metric.set('paid');
    void this.router.navigate([], { queryParams: { productId: product?.id ?? null }, replaceUrl: true });
  }
}
