import type { EChartsCoreOption } from 'echarts/core';
import { NORMALIZED_UNIT_LABELS, NormalizedUnit, PricePoint, StorePriceComparison } from './price-history.service';

import { CATEGORICAL_PALETTE, CHART_INK, OTHER_COLOR, formatMoney } from '../../shared/charts/chart-theme';

export { CATEGORICAL_PALETTE, CHART_INK, OTHER_COLOR, formatMoney };

export type PriceMetric = 'paid' | 'normalized';

const shortDate = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: '2-digit' });

/**
 * Cor de cada supermercado pela ordem do identificador (a cor acompanha a loja, não a
 * posição no ranking).
 */
export function storeColors(storeIds: number[]): Map<number, string> {
  const ordered = [...new Set(storeIds)].sort((a, b) => a - b);
  return new Map(ordered.map((id, index) => [id, CATEGORICAL_PALETTE[index] ?? OTHER_COLOR]));
}

/** Rótulo do valor exibido, conforme a métrica escolhida. */
export function metricLabel(metric: PriceMetric, unit: NormalizedUnit | null): string {
  return metric === 'normalized' && unit ? `Preço por ${NORMALIZED_UNIT_LABELS[unit]}` : 'Preço pago por unidade';
}

/** Linha da evolução do preço: uma série por supermercado, eixo único em reais. */
export function buildPriceTrendOption(points: PricePoint[], metric: PriceMetric): EChartsCoreOption {
  const colors = storeColors(points.map((p) => p.storeId));
  const byStore = new Map<number, { name: string; data: [number, number][] }>();
  for (const point of points) {
    const value = metric === 'normalized' ? point.normalizedPrice : point.effectiveUnitPrice;
    if (value === null) {
      continue;
    }
    const series = byStore.get(point.storeId) ?? { name: point.storeName, data: [] };
    series.data.push([new Date(point.date).getTime(), value]);
    byStore.set(point.storeId, series);
  }

  const multiple = byStore.size > 1;
  const directLabels = byStore.size > 1 && byStore.size <= 4;

  return {
    animation: false,
    color: [...byStore.keys()].map((id) => colors.get(id) ?? OTHER_COLOR),
    grid: { left: 8, right: directLabels ? 96 : 24, top: multiple ? 40 : 16, bottom: 8, containLabel: true },
    legend: multiple
      ? { top: 0, left: 0, type: 'scroll', icon: 'roundRect', itemWidth: 12, itemHeight: 12, textStyle: { color: CHART_INK.secondary } }
      : { show: false },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'line', lineStyle: { color: CHART_INK.baseline } },
      valueFormatter: (value: unknown) => (typeof value === 'number' ? formatMoney(value) : '—'),
    },
    xAxis: {
      type: 'time',
      axisLine: { lineStyle: { color: CHART_INK.baseline } },
      axisTick: { show: false },
      axisLabel: { color: CHART_INK.muted, formatter: (value: number) => shortDate.format(new Date(value)), hideOverlap: true },
      splitLine: { show: false },
    },
    yAxis: {
      type: 'value',
      scale: true,
      axisLabel: { color: CHART_INK.muted, formatter: (value: number) => formatMoney(value) },
      splitLine: { lineStyle: { color: CHART_INK.gridline, width: 1 } },
    },
    series: [...byStore.values()].map((series) => ({
      name: series.name,
      type: 'line',
      data: series.data.sort((a, b) => a[0] - b[0]),
      symbol: 'circle',
      symbolSize: 9,
      lineStyle: { width: 2, cap: 'round', join: 'round' },
      itemStyle: { borderColor: CHART_INK.surface, borderWidth: 2 },
      emphasis: { focus: 'series' },
      endLabel: directLabels ? { show: true, formatter: '{a}', color: CHART_INK.secondary, distance: 8 } : { show: false },
    })),
  };
}

/** Barras horizontais com o último preço por supermercado (menor no topo, rotulado). */
export function buildStoreComparisonOption(stores: StorePriceComparison[]): EChartsCoreOption {
  const ordered = [...stores].sort((a, b) => a.last.price - b.last.price);
  const cheapest = ordered[0]?.last.price;

  return {
    animation: false,
    color: [CATEGORICAL_PALETTE[0]],
    grid: { left: 8, right: 88, top: 8, bottom: 8, containLabel: true },
    tooltip: {
      trigger: 'item',
      formatter: (params: { name: string; value: number }) => `${params.name}<br/>Último preço: <b>${formatMoney(params.value)}</b>`,
    },
    xAxis: {
      type: 'value',
      axisLabel: { color: CHART_INK.muted, formatter: (value: number) => formatMoney(value) },
      splitLine: { lineStyle: { color: CHART_INK.gridline, width: 1 } },
    },
    yAxis: {
      type: 'category',
      inverse: true,
      data: ordered.map((s) => s.storeName),
      axisLine: { lineStyle: { color: CHART_INK.baseline } },
      axisTick: { show: false },
      axisLabel: { color: CHART_INK.secondary },
    },
    series: [
      {
        name: 'Último preço',
        type: 'bar',
        barMaxWidth: 24,
        itemStyle: { borderRadius: [0, 4, 4, 0] },
        data: ordered.map((s) => ({
          value: s.last.price,
          // Rótulo seletivo: somente o menor preço é anotado; os demais estão na tabela e no tooltip.
          label: {
            show: s.last.price === cheapest,
            position: 'right',
            color: CHART_INK.secondary,
            formatter: () => `${formatMoney(s.last.price)} · menor`,
          },
        })),
      },
    ],
  };
}
