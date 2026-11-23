import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { fromIsoDate, toIsoDate } from '../../core/i18n/date-adapter';
import { SearchPicker } from '../../shared/components/search-picker/search-picker';
import { CategoriesService } from '../categories/categories.service';
import { ReceiptPickers, StoreRef, storeDescription, storeName } from '../receipts/pickers';
import { PreferencesService } from '../../core/services/preferences.service';
import { AnalyticsQuery } from './analytics.service';

export type PeriodPreset = 'month' | 'last3' | 'last6' | 'last12' | 'year' | 'lastYear' | 'custom';

export const PERIOD_PRESETS: readonly { value: PeriodPreset; label: string }[] = [
  { value: 'month', label: 'Este mês' },
  { value: 'last3', label: 'Últimos 3 meses' },
  { value: 'last6', label: 'Últimos 6 meses' },
  { value: 'last12', label: 'Últimos 12 meses' },
  { value: 'year', label: 'Este ano' },
  { value: 'lastYear', label: 'Ano anterior' },
  { value: 'custom', label: 'Personalizado' },
];

/** Datas do período (meses completos, terminando hoje quando o período inclui o mês atual). */
export function presetRange(preset: Exclude<PeriodPreset, 'custom'>, today = new Date()): { from: string; to: string } {
  const year = today.getFullYear();
  const month = today.getMonth();
  const todayIso = toIsoDate(today);
  switch (preset) {
    case 'month':
      return { from: toIsoDate(new Date(year, month, 1)), to: todayIso };
    case 'last3':
      return { from: toIsoDate(new Date(year, month - 2, 1)), to: todayIso };
    case 'last6':
      return { from: toIsoDate(new Date(year, month - 5, 1)), to: todayIso };
    case 'last12':
      return { from: toIsoDate(new Date(year, month - 11, 1)), to: todayIso };
    case 'year':
      return { from: toIsoDate(new Date(year, 0, 1)), to: todayIso };
    case 'lastYear':
      return { from: toIsoDate(new Date(year - 1, 0, 1)), to: toIsoDate(new Date(year - 1, 11, 31)) };
  }
}

/** Estado dos filtros, serializável na URL. */
export interface AnalyticsFilterState {
  preset: PeriodPreset;
  from: string | null;
  to: string | null;
  storeId: number | null;
  categoryId: number | null;
}

export const DEFAULT_FILTERS: AnalyticsFilterState = { preset: 'last12', from: null, to: null, storeId: null, categoryId: null };

/** Períodos que podem ser escolhidos como padrão nas preferências (todos exceto o personalizado). */
export const DEFAULT_PERIOD_OPTIONS = PERIOD_PRESETS.filter((p) => p.value !== 'custom');

/** Filtros iniciais com o período padrão escolhido nas preferências (valor inválido → 12 meses). */
export function defaultFilters(preferredPeriod?: string | null): AnalyticsFilterState {
  const preset = DEFAULT_PERIOD_OPTIONS.find((p) => p.value === preferredPeriod)?.value ?? DEFAULT_FILTERS.preset;
  return { ...DEFAULT_FILTERS, preset };
}

export function toAnalyticsQuery(state: AnalyticsFilterState, today = new Date()): AnalyticsQuery {
  const range = state.preset === 'custom' ? { from: state.from, to: state.to } : presetRange(state.preset, today);
  return { ...range, storeId: state.storeId, categoryId: state.categoryId };
}

/** Lê o estado dos filtros a partir de parâmetros de URL (valores inválidos voltam ao padrão). */
export function filtersFromParams(params: Record<string, string | undefined>, defaults = DEFAULT_FILTERS): AnalyticsFilterState {
  const preset = PERIOD_PRESETS.some((p) => p.value === params['period']) ? (params['period'] as PeriodPreset) : defaults.preset;
  const id = (value: string | undefined) => (value && Number(value) > 0 ? Number(value) : null);
  const date = (value: string | undefined) => (value && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : null);
  const from = date(params['from']);
  const to = date(params['to']);
  return {
    preset: preset === 'custom' && (!from || !to) ? defaults.preset : preset,
    from,
    to,
    storeId: id(params['storeId']),
    categoryId: id(params['categoryId']),
  };
}

export function filtersToParams(state: AnalyticsFilterState): Record<string, string | number | null> {
  return {
    period: state.preset,
    from: state.preset === 'custom' ? state.from : null,
    to: state.preset === 'custom' ? state.to : null,
    storeId: state.storeId,
    categoryId: state.categoryId,
  };
}

/** Linha de filtros do dashboard e dos relatórios: período, supermercado e categoria. */
@Component({
  selector: 'app-analytics-filters',
  imports: [ReactiveFormsModule, MatButtonModule, MatDatepickerModule, MatFormFieldModule, MatSelectModule, SearchPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="filters">
      <mat-form-field subscriptSizing="dynamic" class="filters__small">
        <mat-label>Período</mat-label>
        <mat-select [value]="state().preset" (selectionChange)="setPreset($event.value)">
          @for (preset of presets; track preset.value) {
            <mat-option [value]="preset.value">{{ preset.label }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      @if (state().preset === 'custom') {
        <mat-form-field subscriptSizing="dynamic" class="filters__period">
          <mat-label>De / até</mat-label>
          <mat-date-range-input [formGroup]="range" [rangePicker]="picker">
            <input matStartDate formControlName="start" placeholder="Início" />
            <input matEndDate formControlName="end" placeholder="Fim" />
          </mat-date-range-input>
          <mat-datepicker-toggle matIconSuffix [for]="picker" />
          <mat-date-range-picker #picker />
        </mat-form-field>
      }

      <app-search-picker
        class="filters__picker"
        label="Supermercado"
        subscriptSizing="dynamic"
        placeholder="Todos"
        [formControl]="storeControl"
        [search]="pickers.searchStores"
        [display]="storeName"
        [describe]="storeDescription"
      />

      <mat-form-field subscriptSizing="dynamic" class="filters__small">
        <mat-label>Categoria</mat-label>
        <mat-select [value]="state().categoryId" (selectionChange)="update({ categoryId: $event.value })">
          <mat-option [value]="null">Todas</mat-option>
          @for (category of categories(); track category.id) {
            <mat-option [value]="category.id">{{ category.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      @if (hasFilters()) {
        <button mat-button type="button" (click)="reset()">Limpar filtros</button>
      }
    </div>
  `,
  styles: `
    .filters { padding: 0; margin-bottom: 16px; }
  `,
})
export class AnalyticsFilters implements OnInit {
  protected readonly pickers = inject(ReceiptPickers);
  private readonly categoriesService = inject(CategoriesService);
  /** Filtros "limpos": período padrão das preferências, sem supermercado nem categoria. */
  private readonly defaults = defaultFilters(inject(PreferencesService).preferences().analyticsPeriod);

  /** Estado inicial (ex.: da URL). */
  readonly initial = input<AnalyticsFilterState>(DEFAULT_FILTERS);
  /** Nome do supermercado inicial, para exibir no seletor. */
  readonly initialStore = input<StoreRef | null>(null);
  readonly changed = output<AnalyticsFilterState>();

  protected readonly presets = PERIOD_PRESETS;
  protected readonly storeName = storeName;
  protected readonly storeDescription = storeDescription;
  protected readonly state = signal<AnalyticsFilterState>(DEFAULT_FILTERS);
  protected readonly storeControl = new FormControl<StoreRef | null>(null);
  protected readonly range = new FormGroup({
    start: new FormControl<Date | null>(null),
    end: new FormControl<Date | null>(null),
  });

  private readonly categoriesResource = rxResource({ stream: () => this.categoriesService.list() });
  protected readonly categories = computed(() => (this.categoriesResource.hasValue() ? this.categoriesResource.value() : []));
  protected readonly hasFilters = computed(() => {
    const state = this.state();
    return state.preset !== this.defaults.preset || state.storeId !== null || state.categoryId !== null;
  });

  constructor() {
    this.storeControl.valueChanges.pipe(takeUntilDestroyed()).subscribe((store) => this.update({ storeId: store?.id ?? null }));
    this.range.valueChanges.pipe(takeUntilDestroyed()).subscribe(({ start, end }) => {
      if (start && end) {
        this.update({ from: toIsoDate(start), to: toIsoDate(end) });
      }
    });
    effect(() => {
      const store = this.initialStore();
      if (store && this.storeControl.value?.id !== store.id) {
        this.storeControl.setValue(store, { emitEvent: false });
      }
    });
  }

  ngOnInit(): void {
    const initial = this.initial();
    this.state.set(initial);
    if (initial.preset === 'custom' && initial.from && initial.to) {
      this.range.setValue({ start: fromIsoDate(initial.from), end: fromIsoDate(initial.to) }, { emitEvent: false });
    }
  }

  setPreset(preset: PeriodPreset): void {
    if (preset === 'custom') {
      // Personalizado começa com o período atual, para o usuário ajustar.
      const current = toAnalyticsQuery(this.state());
      this.range.setValue({ start: fromIsoDate(current.from!), end: fromIsoDate(current.to!) }, { emitEvent: false });
      this.update({ preset, from: current.from ?? null, to: current.to ?? null });
      return;
    }
    this.update({ preset, from: null, to: null });
  }

  update(change: Partial<AnalyticsFilterState>): void {
    this.state.update((state) => ({ ...state, ...change }));
    this.changed.emit(this.state());
  }

  reset(): void {
    this.storeControl.setValue(null, { emitEvent: false });
    this.state.set(this.defaults);
    this.changed.emit(this.defaults);
  }
}
