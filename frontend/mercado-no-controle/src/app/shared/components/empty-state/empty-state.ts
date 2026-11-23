import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** Estado vazio reutilizável: ícone, título, mensagem e ações projetadas. */
@Component({
  selector: 'app-empty-state',
  imports: [MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="empty-state" role="status">
      <mat-icon class="empty-state__icon" aria-hidden="true">{{ icon() }}</mat-icon>
      <h2 class="empty-state__title">{{ title() }}</h2>
      @if (message()) {
        <p class="empty-state__message">{{ message() }}</p>
      }
      <div class="empty-state__actions"><ng-content /></div>
    </section>
  `,
  styles: `
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      text-align: center;
      gap: 8px;
      padding: 48px 16px;
      color: var(--mat-sys-on-surface-variant);
    }
    .empty-state__icon { font-size: 56px; width: 56px; height: 56px; color: var(--mat-sys-primary); }
    .empty-state__title { margin: 8px 0 0; font: var(--mat-sys-title-large); color: var(--mat-sys-on-surface); }
    .empty-state__message { margin: 0; max-width: 520px; font: var(--mat-sys-body-large); }
    .empty-state__actions:empty { display: none; }
    .empty-state__actions { margin-top: 16px; display: flex; gap: 8px; flex-wrap: wrap; justify-content: center; }
  `,
})
export class EmptyState {
  readonly icon = input('inbox');
  readonly title = input.required<string>();
  readonly message = input<string>();
}
