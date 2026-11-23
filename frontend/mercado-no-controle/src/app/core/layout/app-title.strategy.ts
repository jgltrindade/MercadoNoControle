import { Injectable, inject, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

export const APP_NAME = 'Mercado no Controle';

/**
 * Define o título da aba a partir do `title` das rotas e expõe o título
 * da página atual como signal para o cabeçalho.
 */
@Injectable({ providedIn: 'root' })
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly pageTitle = signal('');

  readonly currentPageTitle = this.pageTitle.asReadonly();

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const routeTitle = this.buildTitle(snapshot) ?? '';
    this.pageTitle.set(routeTitle);
    this.title.setTitle(routeTitle ? `${routeTitle} · ${APP_NAME}` : APP_NAME);
  }
}
