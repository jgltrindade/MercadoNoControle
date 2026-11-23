import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { Router } from '@angular/router';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { StoreRef, toStoreRef } from '../receipts/pickers';
import { StoresService } from '../supermarkets/stores.service';
import { AnalyticsFilterState, AnalyticsFilters, filtersFromParams, filtersToParams, toAnalyticsQuery } from '../dashboard/analytics-filters';
import { AnalyticsService, ExportFormat, ReportColumnType, ReportKey, ReportQuery } from '../dashboard/analytics.service';

export const REPORT_OPTIONS: readonly { value: ReportKey; label: string }[] = [
  { value: 'monthly', label: 'Gastos mensais e anuais' },
  { value: 'stores', label: 'Gastos por supermercado' },
  { value: 'categories', label: 'Gastos por categoria' },
  { value: 'products', label: 'Produtos' },
  { value: 'history', label: 'Histórico de compras' },
  { value: 'comparison', label: 'Comparação entre períodos' },
];

export const RANKING_OPTIONS = [
  { value: 'spent', label: 'Maior gasto acumulado' },
  { value: 'purchases', label: 'Mais comprados' },
  { value: 'increase', label: 'Maior aumento de preço' },
] as const;

type Ranking = (typeof RANKING_OPTIONS)[number]['value'];

const currency = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const number = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 4 });
const percent = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const date = new Intl.DateTimeFormat('pt-BR');

/** Formata uma célula do relatório pelo tipo da coluna (pt-BR). */
export function formatCell(value: string | number | null, type: ReportColumnType): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }
  switch (type) {
    case 'Money':
      return currency.format(Number(value));
    case 'Percent':
      return `${percent.format(Number(value))}%`;
    case 'Number':
      return number.format(Number(value));
    case 'Integer':
      return String(value);
    case 'Date':
      return date.format(new Date(String(value)));
    default:
      return String(value);
  }
}

/** Relatórios (especificação 4.7) com filtros e exportações em CSV, Excel e PDF. */
@Component({
  selector: 'app-reports-page',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTableModule,
    AnalyticsFilters,
    EmptyState,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './reports-page.html',
  styleUrl: './reports-page.scss',
})
export class ReportsPage implements OnInit {
  private readonly service = inject(AnalyticsService);
  private readonly storesService = inject(StoresService);
  private readonly notification = inject(NotificationService);
  private readonly router = inject(Router);

  // Estado vindo da URL.
  readonly report = input<string>();
  readonly ranking = input<string>();
  readonly period = input<string>();
  readonly from = input<string>();
  readonly to = input<string>();
  readonly storeId = input<string>();
  readonly categoryId = input<string>();

  protected readonly reportOptions = REPORT_OPTIONS;
  protected readonly rankingOptions = RANKING_OPTIONS;
  protected readonly formatCell = formatCell;
  protected readonly selected = signal<ReportKey>('monthly');
  protected readonly selectedRanking = signal<Ranking>('spent');
  protected readonly filters = signal<AnalyticsFilterState | null>(null);
  protected readonly initialStore = signal<StoreRef | null>(null);
  protected readonly exporting = signal<ExportFormat | null>(null);

  private readonly query = computed<ReportQuery | undefined>(() => {
    const filters = this.filters();
    if (!filters) return undefined;
    return { ...toAnalyticsQuery(filters), ranking: this.selected() === 'products' ? this.selectedRanking() : null };
  });

  protected readonly table = rxResource({
    params: () => (this.query() ? { key: this.selected(), query: this.query()! } : undefined),
    stream: ({ params }) => this.service.report(params.key, params.query),
  });
  protected readonly data = computed(() => (this.table.hasValue() ? this.table.value() : null));
  protected readonly columnKeys = computed(() => this.data()?.columns.map((c) => c.key) ?? []);
  protected readonly dataRows = computed(() => (this.data()?.rows ?? []).filter((r) => !r.isTotal).length);
  protected readonly loadError = computed(() => {
    const error = this.table.error();
    return error ? toApiError(error).message : null;
  });

  ngOnInit(): void {
    const report = REPORT_OPTIONS.find((r) => r.value === this.report())?.value ?? 'monthly';
    const ranking = RANKING_OPTIONS.find((r) => r.value === this.ranking())?.value ?? 'spent';
    this.selected.set(report);
    this.selectedRanking.set(ranking);
    const initial = filtersFromParams({
      period: this.period(),
      from: this.from(),
      to: this.to(),
      storeId: this.storeId(),
      categoryId: this.categoryId(),
    });
    this.filters.set(initial);
    if (initial.storeId) {
      this.storesService.get(initial.storeId).subscribe({ next: (store) => this.initialStore.set(toStoreRef(store)), error: () => undefined });
    }
  }

  selectReport(report: ReportKey): void {
    this.selected.set(report);
    this.syncUrl();
  }

  selectRanking(ranking: Ranking): void {
    this.selectedRanking.set(ranking);
    this.syncUrl();
  }

  onFiltersChanged(state: AnalyticsFilterState): void {
    this.filters.set(state);
    this.syncUrl();
  }

  isNumeric(type: ReportColumnType): boolean {
    return type !== 'Text' && type !== 'Date';
  }

  /** Exporta exatamente o relatório exibido (mesmo tipo, ranking e filtros). */
  download(format: ExportFormat): void {
    const query = this.query();
    if (!query) return;
    this.exporting.set(format);
    this.service.export(this.selected(), format, query).subscribe({
      next: ({ blob, fileName }) => {
        this.exporting.set(null);
        saveFile(blob, fileName);
      },
      error: (error: unknown) => {
        this.exporting.set(null);
        this.notification.error(toApiError(error).message);
      },
    });
  }

  private syncUrl(): void {
    const filters = this.filters();
    if (!filters) return;
    void this.router.navigate([], {
      queryParams: {
        report: this.selected(),
        ranking: this.selected() === 'products' ? this.selectedRanking() : null,
        ...filtersToParams(filters),
      },
      replaceUrl: true,
    });
  }
}

/** Dispara o download de um arquivo no navegador. */
export function saveFile(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
