import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { EmptyState } from '../empty-state/empty-state';

/**
 * Página provisória de uma funcionalidade ainda não implementada.
 * Recebe os dados pela configuração da rota (`data`) via component input binding.
 */
@Component({
  selector: 'app-placeholder-page',
  imports: [MatCardModule, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card appearance="outlined">
      <app-empty-state [icon]="icon()" [title]="heading()" [message]="description()">
        <span class="phase-badge">Previsto para a {{ phase() }}</span>
      </app-empty-state>
    </mat-card>
  `,
  styles: `
    .phase-badge {
      padding: 4px 12px;
      border-radius: 16px;
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
      font: var(--mat-sys-label-large);
    }
  `,
})
export class PlaceholderPage {
  readonly heading = input.required<string>();
  readonly description = input<string>();
  readonly phase = input.required<string>();
  readonly icon = input('construction');
}
