import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { EChartsCoreOption } from 'echarts/core';
import { NgxEchartsDirective } from 'ngx-echarts';

/**
 * Gráfico ECharts. A descrição (`label`) é exposta para leitores de tela; os dados também
 * devem estar disponíveis em tabela na mesma tela.
 */
@Component({
  selector: 'app-chart',
  imports: [NgxEchartsDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div echarts class="chart" role="img" [attr.aria-label]="label()" [options]="options()" [style.height.px]="height()"></div>`,
  styles: `
    :host { display: block; }
    .chart { width: 100%; }
  `,
})
export class AppChart {
  readonly options = input.required<EChartsCoreOption>();
  readonly label = input.required<string>();
  readonly height = input(300);
}
