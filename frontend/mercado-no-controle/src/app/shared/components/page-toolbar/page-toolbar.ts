import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Barra superior das páginas: descrição à esquerda e ações projetadas à direita. */
@Component({
  selector: 'app-page-toolbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-toolbar">
      @if (description()) {
        <p class="page-toolbar__description">{{ description() }}</p>
      }
      <div class="page-toolbar__actions"><ng-content /></div>
    </div>
  `,
  styles: `
    .page-toolbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      flex-wrap: wrap;
      margin-bottom: 16px;
    }
    .page-toolbar__description { margin: 0; color: var(--mat-sys-on-surface-variant); }
    .page-toolbar__actions { display: flex; gap: 8px; margin-left: auto; }
  `,
})
export class PageToolbar {
  readonly description = input<string>();
}
