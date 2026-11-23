import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { AuthService } from '../../core/auth/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { PreferencesService } from '../../core/services/preferences.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { applyServerErrors, fieldErrorMessage } from '../../shared/forms/form-errors';
import { PAGE_SIZE_OPTIONS } from '../../shared/models/paging';
import { DownloadedFile, saveFile } from '../../shared/utils/download';
import { PASSWORD_MIN_LENGTH, passwordsMatch } from '../auth/login-page';
import { DEFAULT_PERIOD_OPTIONS } from '../dashboard/analytics-filters';
import { SettingsService, formatBytes } from './settings.service';

/** Configurações (especificação 4.8). */
@Component({
  selector: 'app-settings-page',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
    EmptyState,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings-page.html',
  styleUrl: './settings-page.scss',
})
export class SettingsPage {
  private readonly service = inject(SettingsService);
  private readonly auth = inject(AuthService);
  private readonly preferencesService = inject(PreferencesService);
  private readonly notification = inject(NotificationService);
  private readonly confirmService = inject(ConfirmService);

  protected readonly formatBytes = formatBytes;
  protected readonly errorMessage = fieldErrorMessage;
  protected readonly periodOptions = DEFAULT_PERIOD_OPTIONS;
  protected readonly pageSizeOptions = PAGE_SIZE_OPTIONS;
  protected readonly minLength = PASSWORD_MIN_LENGTH;
  protected readonly orphanPreviewLimit = 10;

  protected readonly system = rxResource({ stream: () => this.service.system() });
  protected readonly images = rxResource({ stream: () => this.service.images() });
  protected readonly info = computed(() => (this.system.hasValue() ? this.system.value() : null));
  protected readonly imageSummary = computed(() => (this.images.hasValue() ? this.images.value() : null));
  protected readonly systemError = computed(() => (this.system.error() ? toApiError(this.system.error()).message : null));
  protected readonly imagesError = computed(() => (this.images.error() ? toApiError(this.images.error()).message : null));

  protected readonly user = this.auth.user;
  protected readonly preferences = this.preferencesService.preferences;
  protected readonly downloading = signal<'backup' | 'export' | null>(null);
  protected readonly deleting = signal(false);
  protected readonly savingProfile = signal(false);
  protected readonly savingPassword = signal(false);

  protected readonly profileForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(254)] }),
  });

  protected readonly passwordForm = new FormGroup(
    {
      currentPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      password: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(128)],
      }),
      confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    { validators: passwordsMatch },
  );

  constructor() {
    effect(() => {
      const user = this.user();
      if (user && this.profileForm.pristine) {
        this.profileForm.setValue({ name: user.name, email: user.email });
      }
    });
  }

  setPeriod(value: string): void {
    this.preferencesService.update({ analyticsPeriod: value });
    this.notification.success('Período padrão atualizado.');
  }

  setPageSize(value: number): void {
    this.preferencesService.update({ pageSize: value });
    this.notification.success('Itens por página atualizados.');
  }

  saveProfile(): void {
    if (this.profileForm.invalid) {
      this.profileForm.markAllAsTouched();
      return;
    }
    const { name, email } = this.profileForm.getRawValue();
    this.savingProfile.set(true);
    this.auth.updateProfile(name, email).subscribe({
      next: () => {
        this.savingProfile.set(false);
        this.profileForm.markAsPristine();
        this.notification.success('Dados da conta atualizados.');
      },
      error: (error: unknown) => {
        this.savingProfile.set(false);
        const apiError = toApiError(error);
        if (!applyServerErrors(this.profileForm, apiError)) this.notification.error(apiError.message);
      },
    });
  }

  changePassword(): void {
    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      return;
    }
    const { currentPassword, password } = this.passwordForm.getRawValue();
    this.savingPassword.set(true);
    this.auth.changePassword(currentPassword, password).subscribe({
      next: () => {
        this.savingPassword.set(false);
        this.passwordForm.reset();
        this.notification.success('Senha alterada. Outras sessões abertas foram encerradas.');
      },
      error: (error: unknown) => {
        this.savingPassword.set(false);
        const apiError = toApiError(error);
        if (!applyServerErrors(this.passwordForm, apiError, { newPassword: 'password' })) this.notification.error(apiError.message);
      },
    });
  }

  downloadBackup(): void {
    this.download('backup', this.service.backup(), 'Backup gerado.');
  }

  exportData(): void {
    this.download('export', this.service.exportData(), 'Dados exportados.');
  }

  deleteOrphans(): void {
    const summary = this.imageSummary();
    if (!summary?.orphans.length) return;
    this.confirmService
      .confirm({
        title: 'Excluir imagens órfãs?',
        message: `${summary.orphans.length} imagem(ns) sem nota associada (${formatBytes(summary.orphanBytes)}) serão excluídas definitivamente. Faça um backup antes, se quiser guardá-las.`,
        confirmLabel: 'Excluir',
        destructive: true,
      })
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.deleting.set(true);
        this.service.deleteOrphans().subscribe({
          next: (result) => {
            this.deleting.set(false);
            this.notification.success(`${result.deletedFiles} imagem(ns) excluída(s), ${formatBytes(result.freedBytes)} liberados.`);
            this.images.reload();
          },
          error: (error: unknown) => {
            this.deleting.set(false);
            this.notification.error(toApiError(error).message);
          },
        });
      });
  }

  private download(kind: 'backup' | 'export', request: Observable<DownloadedFile>, message: string): void {
    this.downloading.set(kind);
    request.subscribe({
      next: ({ blob, fileName }) => {
        this.downloading.set(null);
        saveFile(blob, fileName);
        this.notification.success(message);
      },
      error: (error: unknown) => {
        this.downloading.set(null);
        this.notification.error(toApiError(error).message);
      },
    });
  }
}
