import type { EChartsCoreOption } from 'echarts/core';

/**
 * Paleta categórica (identidade) em ordem fixa, validada para daltonismo contra a superfície
 * clara do app. Nunca é ciclada: além da 8ª série, usa-se a cor "outras".
 */
export const CATEGORICAL_PALETTE = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#6250d6', '#e34948'];
export const OTHER_COLOR = '#898781';

/** Tinta e elementos de apoio (texto nunca usa a cor da série). */
export const CHART_INK = {
  surface: '#fbfdf7',
  secondary: '#52514e',
  muted: '#6b6a65',
  gridline: '#e1e0d9',
  baseline: '#c3c2b7',
};

const currency = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: 2, maximumFractionDigits: 4 });
const compactCurrency = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', notation: 'compact', maximumFractionDigits: 1 });
const wholeCurrency = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 });

export function formatMoney(value: number): string {
  return currency.format(value);
}

/** Valores de eixo: compactos a partir de 10 mil; sem ",00" em marcas inteiras; centavos só quando existem. */
export function formatAxisMoney(value: number): string {
  if (Math.abs(value) >= 10_000) return compactCurrency.format(value);
  return Number.isInteger(value) ? wholeCurrency.format(value) : currency.format(value);
}

export interface BarItem {
  label: string;
  value: number;
}

/** Barras horizontais de uma série (distribuições), do maior para o menor; rotula apenas a maior. */
export function buildHorizontalBarOption(items: BarItem[], seriesName: string): EChartsCoreOption {
  const ordered = [...items].sort((a, b) => b.value - a.value);
  const largest = ordered[0]?.value;
  return {
    animation: false,
    color: [CATEGORICAL_PALETTE[0]],
    grid: { left: 8, right: 96, top: 8, bottom: 8, containLabel: true },
    tooltip: {
      trigger: 'item',
      formatter: (params: { name: string; value: number }) => `${params.name}<br/>${seriesName}: <b>${formatMoney(params.value)}</b>`,
    },
    xAxis: {
      type: 'value',
      axisLabel: { color: CHART_INK.muted, hideOverlap: true, formatter: (value: number) => formatAxisMoney(value) },
      splitLine: { lineStyle: { color: CHART_INK.gridline, width: 1 } },
    },
    yAxis: {
      type: 'category',
      inverse: true,
      data: ordered.map((item) => item.label),
      axisLine: { lineStyle: { color: CHART_INK.baseline } },
      axisTick: { show: false },
      axisLabel: { color: CHART_INK.secondary, width: 160, overflow: 'truncate' },
    },
    series: [
      {
        name: seriesName,
        type: 'bar',
        barMaxWidth: 24,
        itemStyle: { borderRadius: [0, 4, 4, 0] },
        data: ordered.map((item) => ({
          value: item.value,
          label: { show: item.value === largest, position: 'right', color: CHART_INK.secondary, formatter: () => formatMoney(item.value) },
        })),
      },
    ],
  };
}

/** Colunas verticais de uma série (evolução mensal); rotula apenas a coluna mais recente. */
export function buildColumnOption(labels: string[], values: number[], seriesName: string): EChartsCoreOption {
  return {
    animation: false,
    color: [CATEGORICAL_PALETTE[0]],
    grid: { left: 8, right: 16, top: 24, bottom: 8, containLabel: true },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      valueFormatter: (value: unknown) => (typeof value === 'number' ? formatMoney(value) : '—'),
    },
    xAxis: {
      type: 'category',
      data: labels,
      axisLine: { lineStyle: { color: CHART_INK.baseline } },
      axisTick: { show: false },
      axisLabel: { color: CHART_INK.muted, hideOverlap: true },
    },
    yAxis: {
      type: 'value',
      axisLabel: { color: CHART_INK.muted, hideOverlap: true, formatter: (value: number) => formatAxisMoney(value) },
      splitLine: { lineStyle: { color: CHART_INK.gridline, width: 1 } },
    },
    series: [
      {
        name: seriesName,
        type: 'bar',
        barMaxWidth: 24,
        itemStyle: { borderRadius: [4, 4, 0, 0] },
        data: values.map((value, index) => ({
          value,
          label: { show: index === values.length - 1 && value > 0, position: 'top', color: CHART_INK.secondary, formatter: () => formatMoney(value) },
        })),
      },
    ],
  };
}

export interface GroupedSeries {
  name: string;
  values: number[];
}

/** Barras horizontais agrupadas (ex.: mês anterior × mês atual por categoria), com legenda. */
export function buildGroupedBarOption(categories: string[], series: GroupedSeries[]): EChartsCoreOption {
  return {
    animation: false,
    color: series.map((_, index) => CATEGORICAL_PALETTE[index] ?? OTHER_COLOR),
    grid: { left: 8, right: 24, top: 36, bottom: 8, containLabel: true },
    legend: { top: 0, left: 0, icon: 'roundRect', itemWidth: 12, itemHeight: 12, textStyle: { color: CHART_INK.secondary } },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      valueFormatter: (value: unknown) => (typeof value === 'number' ? formatMoney(value) : '—'),
    },
    xAxis: {
      type: 'value',
      axisLabel: { color: CHART_INK.muted, hideOverlap: true, formatter: (value: number) => formatAxisMoney(value) },
      splitLine: { lineStyle: { color: CHART_INK.gridline, width: 1 } },
    },
    yAxis: {
      type: 'category',
      inverse: true,
      data: categories,
      axisLine: { lineStyle: { color: CHART_INK.baseline } },
      axisTick: { show: false },
      axisLabel: { color: CHART_INK.secondary, width: 160, overflow: 'truncate' },
    },
    series: series.map((item) => ({
      name: item.name,
      type: 'bar',
      barMaxWidth: 16,
      barGap: '20%',
      itemStyle: { borderRadius: [0, 4, 4, 0] },
      data: item.values,
    })),
  };
}
