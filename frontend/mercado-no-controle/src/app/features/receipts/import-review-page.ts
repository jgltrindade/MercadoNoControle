import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  InjectionToken,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Router, RouterLink } from '@angular/router';
import { Subscription, exhaustMap, takeWhile, timer } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { IMPORT_STATUS_LABELS, ImportsService, ReceiptImport, isImportInProgress } from './imports.service';
import { ReceiptFormPage } from './receipt-form-page';

/** Intervalo (ms) de consulta do status enquanto a leitura está em andamento. */
export const IMPORT_POLL_INTERVAL = new InjectionToken<number>('IMPORT_POLL_INTERVAL', {
  providedIn: 'root',
  factory: () => 2000,
});

/**
 * Acompanhamento e conferência de uma importação (rota /receipts/imports/:importId):
 * mostra o progresso da leitura e, ao final, a imagem ao lado do formulário pré-preenchido.
 */
@Component({
  selector: 'app-import-review-page',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
    EmptyState,
    ReceiptFormPage,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './import-review-page.html',
  styleUrl: './import-review-page.scss',
})
export class ImportReviewPage implements OnInit {
  private readonly service = inject(ImportsService);
  private readonly notification = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly pollInterval = inject(IMPORT_POLL_INTERVAL);
  private polling?: Subscription;

  readonly importId = input.required<string>();

  protected readonly current = signal<ReceiptImport | null>(null);
  protected readonly loadError = signal<{ status: number; message: string } | null>(null);
  protected readonly manualEntry = signal(false);
  protected readonly busy = signal(false);
  protected readonly inProgress = computed(() => {
    const current = this.current();
    return !current || isImportInProgress(current.status);
  });
  protected readonly statusLabel = computed(() => {
    const current = this.current();
    return current ? IMPORT_STATUS_LABELS[current.status] : '';
  });
  protected readonly imageUrl = computed(() => this.service.imageUrl(this.importId()));

  constructor() {
    this.destroyRef.onDestroy(() => this.polling?.unsubscribe());
  }

  ngOnInit(): void {
    this.poll();
  }

  reprocess(): void {
    this.busy.set(true);
    this.service.reprocess(this.importId()).subscribe({
      next: (updated) => {
        this.busy.set(false);
        this.manualEntry.set(false);
        this.current.set(updated);
        this.poll();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.notification.error(toApiError(error).message);
      },
    });
  }

  discard(): void {
    this.busy.set(true);
    this.service.discard(this.importId()).subscribe({
      next: () => {
        this.notification.success('Leitura descartada.');
        void this.router.navigate(['/receipts/new']);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.notification.error(toApiError(error).message);
      },
    });
  }

  /** Consulta o status até a leitura terminar. */
  private poll(): void {
    this.polling?.unsubscribe();
    this.polling = timer(0, this.pollInterval)
      .pipe(
        // exhaustMap: não sobrepõe consultas quando a resposta demora mais que o intervalo.
        exhaustMap(() => this.service.get(this.importId())),
        takeWhile((current) => isImportInProgress(current.status), true),
      )
      .subscribe({
        next: (current) => {
          this.loadError.set(null);
          this.current.set(current);
        },
        error: (error: unknown) => {
          const apiError = toApiError(error);
          this.loadError.set({ status: apiError.status, message: apiError.message });
        },
      });
  }
}
