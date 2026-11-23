import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { toIsoDate } from '../../core/i18n/date-adapter';
import { buildColumnOption, buildGroupedBarOption, buildHorizontalBarOption } from '../../shared/charts/chart-theme';
import { AppChart } from '../../shared/components/chart/chart';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { StoreRef, toStoreRef } from '../receipts/pickers';
import { StoresService } from '../supermarkets/stores.service';
import {
  AnalyticsFilterState,
  AnalyticsFilters,
  filtersFromParams,
  filtersToParams,
  toAnalyticsQuery,
} from './analytics-filters';
import { AnalyticsQuery, AnalyticsService, CategoryTotal, MonthlyTotal } from './analytics.service';

const monthFormat = new Intl.DateTimeFormat('pt-BR', { month: 'short', year: '2-digit' });

export function monthLabel(month: MonthlyTotal): string {
  return monthFormat.format(new Date(month.year, month.month - 1, 1)).replace('.', '').replace(' de ', '/');
}

/** Gasto por categoria no mês anterior × mês atual, alinhado pela categoria. */
export function monthComparison(previous: CategoryTotal[], current: CategoryTotal[]) {
  const names = [...new Set([...current, ...previous].map((c) => c.categoryName))];
  const value = (list: CategoryTotal[], name: string) => list.find((c) => c.categoryName === name)?.total ?? 0;
  const rows = names
    .map((name) => ({ name, previous: value(previous, name), current: value(current, name) }))
    .sort((a, b) => b.current - a.current || b.previous - a.previous);
  return {
    categories: rows.map((r) => r.name),
    previous: rows.map((r) => r.previous),
    current: rows.map((r) => r.current),
  };
}

/** Dashboard (especificação 4.1): somente compras confirmadas. */
@Component({
  selector: 'app-dashboard-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    AnalyticsFilters,
    AppChart,
    EmptyState,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss',
})
export class DashboardPage implements OnInit {
  private readonly service = inject(AnalyticsService);
  private readonly storesService = inject(StoresService);
  private readonly router = inject(Router);

  // Filtros vindos da URL (component input binding de query params).
  readonly period = input<string>();
  readonly from = input<string>();
  readonly to = input<string>();
  readonly storeId = input<string>();
  readonly categoryId = input<string>();

  protected readonly filters = signal<AnalyticsFilterState | null>(null);
  protected readonly initialStore = signal<StoreRef | null>(null);
  private readonly query = computed<AnalyticsQuery | undefined>(() => {
    const filters = this.filters();
    return filters ? toAnalyticsQuery(filters) : undefined;
  });
  protected readonly reportParams = computed(() => (this.filters() ? filtersToParams(this.filters()!) : {}));

  protected readonly summary = rxResource({ params: () => this.query(), stream: ({ params }) => this.service.summary(params) });
  protected readonly monthly = rxResource({ params: () => this.query(), stream: ({ params }) => this.service.monthly(params) });
  protected readonly stores = rxResource({ params: () => this.query(), stream: ({ params }) => this.service.stores(params) });
  protected readonly categories = rxResource({ params: () => this.query(), stream: ({ params }) => this.service.categories(params) });
  protected readonly products = rxResource({ params: () => this.query(), stream: ({ params }) => this.service.products(params, 10) });
  protected readonly months = rxResource({
    params: () => this.query(),
    stream: ({ params }) => {
      const today = new Date();
      const current = { ...params, from: toIsoDate(new Date(today.getFullYear(), today.getMonth(), 1)), to: toIsoDate(today) };
      const previous = {
        ...params,
        from: toIsoDate(new Date(today.getFullYear(), today.getMonth() - 1, 1)),
        to: toIsoDate(new Date(today.getFullYear(), today.getMonth(), 0)),
      };
      return forkJoin({ previous: this.service.categories(previous), current: this.service.categories(current) });
    },
  });

  protected readonly data = computed(() => (this.summary.hasValue() ? this.summary.value() : null));
  protected readonly loadError = computed(() => {
    const error = this.summary.error();
    return error ? toApiError(error).message : null;
  });

  protected readonly monthlyOption = computed(() => {
    const months = this.monthly.hasValue() ? this.monthly.value() : [];
    return buildColumnOption(months.map(monthLabel), months.map((m) => m.total), 'Total gasto');
  });
  protected readonly storesOption = computed(() =>
    buildHorizontalBarOption((this.stores.hasValue() ? this.stores.value() : []).map((s) => ({ label: s.storeName, value: s.total })), 'Total gasto'),
  );
  protected readonly categoriesOption = computed(() =>
    buildHorizontalBarOption(
      (this.categories.hasValue() ? this.categories.value() : []).map((c) => ({ label: c.categoryName, value: c.total })),
      'Total gasto',
    ),
  );
  protected readonly productsOption = computed(() =>
    buildHorizontalBarOption(
      (this.products.hasValue() ? this.products.value() : []).map((p) => ({ label: p.productName, value: p.total })),
      'Total gasto',
    ),
  );
  protected readonly monthComparison = computed(() =>
    this.months.hasValue() ? monthComparison(this.months.value().previous, this.months.value().current) : null,
  );
  protected readonly monthComparisonOption = computed(() => {
    const comparison = this.monthComparison();
    return comparison
      ? buildGroupedBarOption(comparison.categories, [
          { name: 'Mês anterior', values: comparison.previous },
          { name: 'Mês atual', values: comparison.current },
        ])
      : {};
  });
  protected readonly storeCount = computed(() => (this.stores.hasValue() ? this.stores.value().length : 0));
  protected readonly categoryCount = computed(() => (this.categories.hasValue() ? this.categories.value().length : 0));
  protected readonly productCount = computed(() => (this.products.hasValue() ? this.products.value().length : 0));

  ngOnInit(): void {
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

  /** Parâmetros para abrir o relatório correspondente com os mesmos filtros (tabela com os dados do gráfico). */
  reportLink(report: string): Record<string, string | number | null> {
    return { report, ...this.reportParams() };
  }

  comparisonLink(): Record<string, string | number | null> {
    const params = this.reportParams();
    return { report: 'comparison', period: 'month', storeId: params['storeId'] ?? null, categoryId: params['categoryId'] ?? null };
  }

  onFiltersChanged(state: AnalyticsFilterState): void {
    this.filters.set(state);
    void this.router.navigate([], { queryParams: filtersToParams(state), replaceUrl: true });
  }
}
