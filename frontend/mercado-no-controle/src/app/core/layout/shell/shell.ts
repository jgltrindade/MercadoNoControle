import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenav, MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatMenuModule } from '@angular/material/menu';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../../auth/auth.service';
import { NotificationService } from '../../services/notification.service';
import { ApiStatus } from '../api-status/api-status';
import { APP_NAME, AppTitleStrategy } from '../app-title.strategy';
import { NAVIGATION_ITEMS } from '../navigation';

/** Layout principal: sidebar (desktop) / menu deslizante (telas menores), cabeçalho e conteúdo. */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatToolbarModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    ApiStatus,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Shell {
  private readonly titleStrategy = inject(AppTitleStrategy);
  private readonly sidenav = viewChild.required(MatSidenav);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notification = inject(NotificationService);

  /** Usuário da sessão (somente com a autenticação ativa). */
  readonly user = this.auth.user;

  readonly appName = APP_NAME;
  readonly navigationItems = NAVIGATION_ITEMS;
  readonly pageTitle = this.titleStrategy.currentPageTitle;

  readonly isHandset = toSignal(
    inject(BreakpointObserver)
      .observe([Breakpoints.XSmall, Breakpoints.Small])
      .pipe(map((result) => result.matches)),
    { initialValue: false },
  );

  logout(): void {
    this.auth.logout().subscribe({
      next: () => void this.router.navigate(['/login']),
      error: () => this.notification.error('Não foi possível encerrar a sessão. Tente novamente.'),
    });
  }

  closeOnHandset(): void {
    if (this.isHandset()) {
      void this.sidenav().close();
    }
  }
}
