import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { formatMoney } from './chart-options';
import { NORMALIZED_UNIT_LABELS, PriceReference, PriceSummary } from './price-history.service';

interface Tile {
  label: string;
  value: string;
  detail: string | null;
}

/** Indicadores do histórico: último, menor, maior e médio preço e variação no período. */
@Component({
  selector: 'app-price-summary-tiles',
  imports: [DecimalPipe, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tiles">
      @for (tile of tiles(); track tile.label) {
        <div class="tile">
          <span class="tile__label">{{ tile.label }}</span>
          <span class="tile__value">{{ tile.value }}</span>
          @if (tile.detail) {
            <span class="tile__detail">{{ tile.detail }}</span>
          }
        </div>
      }
      @if (summary().variationPercent !== null) {
        <div class="tile">
          <span class="tile__label">Variação no período</span>
          <span class="tile__value tile__value--variation">
            <mat-icon aria-hidden="true">{{ variationIcon() }}</mat-icon>
            {{ summary().variationPercent! > 0 ? '+' : '' }}{{ summary().variationPercent | number: '1.0-2' }}%
          </span>
          <span class="tile__detail">{{ variationText() }}</span>
        </div>
      }
    </div>
    @if (normalizedText(); as text) {
      <p class="normalized" data-testid="normalized">{{ text }}</p>
    }
  `,
  styles: `
    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; }
    .tile { display: flex; flex-direction: column; gap: 2px; padding: 12px 16px; border-radius: var(--app-radius); background: var(--mat-sys-surface-container-low); }
    .tile__label { font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .tile__value { font: var(--mat-sys-headline-small); color: var(--mat-sys-on-surface); }
    .tile__value--variation { display: flex; align-items: center; gap: 4px; }
    .tile__detail { font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); }
    .normalized { margin: 12px 0 0; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class PriceSummaryTiles {
  readonly summary = input.required<PriceSummary>();

  protected readonly tiles = computed<Tile[]>(() => {
    const summary = this.summary();
    const tiles: Tile[] = [];
    const add = (label: string, reference: PriceReference | null) => {
      if (reference) {
        tiles.push({ label, value: formatMoney(reference.price), detail: describe(reference) });
      }
    };
    add('Último preço pago', summary.last);
    add('Menor preço', summary.lowest);
    add('Maior preço', summary.highest);
    if (summary.average !== null) {
      tiles.push({ label: 'Preço médio', value: formatMoney(summary.average), detail: `${summary.count} compra(s)` });
    }
    return tiles;
  });

  protected readonly variationIcon = computed(() => {
    const variation = this.summary().variationPercent ?? 0;
    return variation > 0 ? 'trending_up' : variation < 0 ? 'trending_down' : 'trending_flat';
  });

  protected readonly variationText = computed(() => {
    const variation = this.summary().variationPercent ?? 0;
    return variation > 0 ? 'aumento desde a 1ª compra' : variation < 0 ? 'queda desde a 1ª compra' : 'sem variação';
  });

  protected readonly normalizedText = computed(() => {
    const { normalizedUnit, lastNormalized, lowestNormalized, averageNormalized } = this.summary();
    if (!normalizedUnit || lastNormalized === null) {
      return null;
    }
    const unit = NORMALIZED_UNIT_LABELS[normalizedUnit];
    return (
      `Preço por ${unit}: último ${formatMoney(lastNormalized)}` +
      (lowestNormalized !== null ? ` · menor ${formatMoney(lowestNormalized)}` : '') +
      (averageNormalized !== null ? ` · médio ${formatMoney(averageNormalized)}` : '')
    );
  });
}

const dateFormat = new Intl.DateTimeFormat('pt-BR');

function describe(reference: PriceReference): string {
  return `${dateFormat.format(new Date(reference.date))} · ${reference.storeName}`;
}
