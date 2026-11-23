import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { toApiError } from '../../api/api-error';
import { HealthService, SystemStatus } from '../../services/health.service';

type ApiState = 'checking' | 'online' | 'degraded' | 'offline';

const LABELS: Record<ApiState, string> = {
  checking: 'Verificando API…',
  online: 'API online',
  degraded: 'Banco indisponível',
  offline: 'API offline',
};

const ICONS: Record<ApiState, string> = {
  checking: 'sync',
  online: 'cloud_done',
  degraded: 'warning',
  offline: 'cloud_off',
};

/** Indicador de conectividade com o backend, exibido no cabeçalho. */
@Component({
  selector: 'app-api-status',
  imports: [MatIconModule, MatButtonModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      mat-button
      type="button"
      class="api-status"
      [class]="'api-status--' + state()"
      [matTooltip]="tooltip()"
      [disabled]="state() === 'checking'"
      (click)="check()"
      data-testid="api-status"
    >
      <mat-icon>{{ icon() }}</mat-icon>
      <span class="api-status__label">{{ label() }}</span>
    </button>
  `,
  styles: `
    .api-status { --mat-button-text-label-text-color: currentColor; }
    .api-status--online { color: var(--mat-sys-primary); }
    .api-status--degraded { color: #b26a00; }
    .api-status--offline { color: var(--mat-sys-error); }
    .api-status--checking { color: var(--mat-sys-on-surface-variant); }
    @media (max-width: 599px) { .api-status__label { display: none; } }
  `,
})
export class ApiStatus implements OnInit {
  private readonly healthService = inject(HealthService);

  readonly state = signal<ApiState>('checking');
  private readonly details = signal<string>('');

  readonly label = computed(() => LABELS[this.state()]);
  readonly icon = computed(() => ICONS[this.state()]);
  readonly tooltip = computed(() => {
    const details = this.details();
    return details ? `${details} — clique para verificar novamente` : 'Clique para verificar novamente';
  });

  ngOnInit(): void {
    this.check();
  }

  check(): void {
    this.state.set('checking');
    this.healthService.getStatus().subscribe({
      next: (status) => this.applyStatus(status),
      error: (error: unknown) => {
        // A API responde 503 com o próprio status quando o banco está indisponível.
        if (error instanceof HttpErrorResponse && error.status === 503 && isSystemStatus(error.error)) {
          this.applyStatus(error.error);
          return;
        }
        this.state.set('offline');
        this.details.set(toApiError(error).message);
      },
    });
  }

  private applyStatus(status: SystemStatus): void {
    this.state.set(status.databaseAvailable ? 'online' : 'degraded');
    this.details.set(`Versão ${status.version} · ${status.environment}`);
  }
}

function isSystemStatus(value: unknown): value is SystemStatus {
  return typeof value === 'object' && value !== null && 'databaseAvailable' in value;
}
