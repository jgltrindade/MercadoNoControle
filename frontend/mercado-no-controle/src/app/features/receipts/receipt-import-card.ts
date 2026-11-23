import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { toApiError } from '../../core/api/api-error';
import { IMPORT_STATUS_LABELS, ImportsService, ReceiptImportStatus } from './imports.service';

/** Limite de tamanho aceito pela leitura automática. */
export const MAX_AI_IMAGE_BYTES = 5 * 1024 * 1024;
const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

/** Envio da fotografia da nota para leitura automática (topo da tela "Nova compra"). */
@Component({
  selector: 'app-receipt-import-card',
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card appearance="outlined" class="import-card">
      <mat-card-header>
        <mat-icon mat-card-avatar class="import-card__icon">photo_camera</mat-icon>
        <mat-card-title>Ler nota por foto</mat-card-title>
        <mat-card-subtitle>Envie a fotografia da nota; os dados lidos serão apresentados para conferência.</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (readerStatus.isLoading()) {
          <mat-progress-bar mode="indeterminate" />
        } @else if (available()) {
          <input
            #fileInput
            type="file"
            hidden
            accept="image/jpeg,image/png,image/webp"
            capture="environment"
            (change)="onFileSelected($event)"
          />
          <button mat-flat-button type="button" [disabled]="uploading()" (click)="fileInput.click()" data-testid="upload-button">
            <mat-icon>upload</mat-icon>
            {{ uploading() ? 'Enviando…' : 'Enviar foto da nota' }}
          </button>
          <span class="muted hint">JPEG, PNG ou WebP até 5 MB.</span>
        } @else {
          <p class="muted unavailable" role="status">
            <mat-icon class="inline-icon">info</mat-icon>
            {{ unavailableMessage() }}
          </p>
        }
        @if (error()) {
          <p class="form-error" role="alert">{{ error() }}</p>
        }

        @if (openImports().length) {
          <h3 class="open-title">Leituras em aberto</h3>
          <ul class="open-list">
            @for (item of openImports(); track item.id) {
              <li>
                <a class="link" [routerLink]="['/receipts/imports', item.id]">
                  {{ item.originalFileName ?? 'Imagem' }} · {{ item.createdAt | date: 'dd/MM HH:mm' }}
                </a>
                <span class="status-badge" [class.status-badge--draft]="item.status !== 'AwaitingReview'">
                  {{ statusLabel(item.status) }}
                </span>
              </li>
            }
          </ul>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .import-card__icon { color: var(--mat-sys-primary); }
    .hint { margin-left: 12px; }
    .unavailable { display: flex; gap: 6px; align-items: flex-start; margin: 0; }
    .form-error { margin-top: 12px; }
    .open-title { margin: 16px 0 4px; font: var(--mat-sys-title-small); }
    .open-list { list-style: none; margin: 0; padding: 0; }
    .open-list li { display: flex; gap: 12px; align-items: center; padding: 4px 0; }
  `,
})
export class ReceiptImportCard {
  private readonly service = inject(ImportsService);
  private readonly router = inject(Router);

  protected readonly readerStatus = rxResource({ stream: () => this.service.readerStatus() });
  private readonly imports = rxResource({ stream: () => this.service.listOpen() });

  protected readonly available = computed(() => this.readerStatus.hasValue() && this.readerStatus.value().available);
  protected readonly unavailableMessage = computed(() => {
    if (this.readerStatus.error()) {
      return 'Não foi possível verificar a leitura automática.';
    }
    return (this.readerStatus.hasValue() && this.readerStatus.value().message) || 'A leitura automática está indisponível.';
  });
  protected readonly openImports = computed(() => (this.imports.hasValue() ? this.imports.value() : []));
  protected readonly uploading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly statusLabel = (status: ReceiptImportStatus) => IMPORT_STATUS_LABELS[status];

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    this.error.set(null);
    if (!file) {
      return;
    }
    if (!IMAGE_TYPES.includes(file.type)) {
      this.error.set('Formato não suportado. Envie JPEG, PNG ou WebP.');
      return;
    }
    if (file.size > MAX_AI_IMAGE_BYTES) {
      this.error.set('A imagem excede 5 MB, o limite da leitura automática. Envie uma foto com resolução menor.');
      return;
    }

    this.uploading.set(true);
    this.service.upload(file).subscribe({
      next: (created) => void this.router.navigate(['/receipts/imports', created.id]),
      error: (error: unknown) => {
        this.uploading.set(false);
        this.error.set(toApiError(error).message);
      },
    });
  }
}
