import { LOCALE_ID, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { DEFAULT_PREFERENCES, PreferencesService } from '../../core/services/preferences.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { fileNameFromResponse } from '../../shared/utils/download';
import { settle } from '../../testing/settle';
import { defaultFilters, filtersFromParams } from '../dashboard/analytics-filters';
import { SettingsPage } from './settings-page';
import { ImagesSummary, SettingsService, SystemInfo, formatBytes } from './settings.service';
import { HttpHeaders, HttpResponse } from '@angular/common/http';

registerLocaleData(localePt);

const system: SystemInfo = {
  version: '1.0.0',
  environment: 'Production',
  authenticationEnabled: true,
  databaseFile: 'C:/dados/mercado-no-controle.db',
  databaseSizeBytes: 2_621_440,
  uploadsDirectory: 'C:/dados/uploads',
  counts: { stores: 2, categories: 10, products: 5, confirmedReceipts: 12, draftReceipts: 1, priceObservations: 40, openImports: 0 },
  reader: { available: false, provider: 'Nenhum', model: null, effort: null, timeoutSeconds: 180, message: 'A leitura automática não está configurada.' },
};

const images: ImagesSummary = {
  totalFiles: 3,
  totalBytes: 3_000_000,
  referencedFiles: 1,
  missingFiles: 0,
  orphanBytes: 2_000_000,
  orphans: [
    { path: 'receipts/2026/11/a.jpg', sizeBytes: 1_000_000, modifiedAt: '2026-11-20T10:00:00Z' },
    { path: 'receipts/2026/11/b.jpg', sizeBytes: 1_000_000, modifiedAt: '2026-11-21T10:00:00Z' },
  ],
};

describe('settings helpers', () => {
  it('formats sizes', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1536)).toBe('1,5 KB');
    expect(formatBytes(2_621_440)).toBe('2,5 MB');
  });

  it('reads the file name from Content-Disposition', () => {
    const response = (header: string) => new HttpResponse<Blob>({ body: new Blob(), headers: new HttpHeaders({ 'Content-Disposition': header }) });
    expect(fileNameFromResponse(response("attachment; filename=a.zip; filename*=UTF-8''backup%20nov.zip"), 'x.zip')).toBe('backup nov.zip');
    expect(fileNameFromResponse(response('attachment; filename="dados.zip"'), 'x.zip')).toBe('dados.zip');
    expect(fileNameFromResponse(new HttpResponse<Blob>({ body: new Blob() }), 'x.zip')).toBe('x.zip');
  });

  it('uses the preferred default period in the analytics filters', () => {
    expect(defaultFilters('year').preset).toBe('year');
    expect(defaultFilters('custom').preset).toBe('last12');
    expect(defaultFilters('nope').preset).toBe('last12');
    expect(filtersFromParams({}, defaultFilters('month')).preset).toBe('month');
    expect(filtersFromParams({ period: 'last3' }, defaultFilters('month')).preset).toBe('last3');
  });
});

describe('PreferencesService', () => {
  beforeEach(() => localStorage.clear());

  it('persists valid preferences and ignores invalid values', () => {
    TestBed.configureTestingModule({});
    const service = TestBed.inject(PreferencesService);
    expect(service.preferences()).toEqual(DEFAULT_PREFERENCES);

    service.update({ pageSize: 50, analyticsPeriod: 'year' });
    expect(JSON.parse(localStorage.getItem('mnc.preferences')!)).toEqual({ pageSize: 50, analyticsPeriod: 'year' });

    service.update({ pageSize: 7 });
    expect(service.preferences().pageSize).toBe(DEFAULT_PREFERENCES.pageSize);
  });

  it('reads stored preferences when created', () => {
    localStorage.setItem('mnc.preferences', JSON.stringify({ pageSize: 100, analyticsPeriod: 'month' }));
    TestBed.configureTestingModule({});
    expect(TestBed.inject(PreferencesService).preferences()).toEqual({ pageSize: 100, analyticsPeriod: 'month' });
  });
});

describe('SettingsService', () => {
  it('calls the settings endpoints', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const service = TestBed.inject(SettingsService);
    const http = TestBed.inject(HttpTestingController);

    service.system().subscribe();
    http.expectOne('/api/settings/system').flush(system);
    service.images().subscribe();
    http.expectOne('/api/settings/images').flush(images);
    service.deleteOrphans().subscribe();
    expect(http.expectOne('/api/settings/images/orphans').request.method).toBe('DELETE');

    let fileName = '';
    service.backup().subscribe((file) => (fileName = file.fileName));
    const backup = http.expectOne('/api/settings/backup');
    expect(backup.request.responseType).toBe('blob');
    backup.flush(new Blob(['zip']));
    expect(fileName).toBe('mercado-no-controle-backup.zip');

    service.exportData().subscribe();
    http.expectOne('/api/settings/export').flush(new Blob(['zip']));
    http.verify();
  });
});

describe('SettingsPage', () => {
  let fixture: ComponentFixture<SettingsPage>;
  let http: HttpTestingController;
  const notification = { success: vi.fn(), error: vi.fn() };
  const confirm = vi.fn();
  const auth = {
    user: signal<{ id: number; name: string; email: string; lastLoginAt: string | null } | null>(null),
    updateProfile: vi.fn(),
    changePassword: vi.fn(),
  };

  async function setup(withUser = true) {
    localStorage.clear();
    notification.success.mockReset();
    notification.error.mockReset();
    confirm.mockReset();
    auth.user.set(withUser ? { id: 1, name: 'Pessoa', email: 'eu@example.com', lastLoginAt: '2026-11-23T10:00:00Z' } : null);
    TestBed.configureTestingModule({
      imports: [SettingsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: LOCALE_ID, useValue: 'pt-BR' },
        { provide: AuthService, useValue: auth },
        { provide: NotificationService, useValue: notification },
        { provide: ConfirmService, useValue: { confirm } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SettingsPage);
    await settle();
    http.expectOne('/api/settings/system').flush(system);
    http.expectOne('/api/settings/images').flush(images);
    await settle();
  }

  afterEach(() => http.verify());

  const text = () => (fixture.nativeElement as HTMLElement).textContent!;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const page = () => fixture.componentInstance as any;

  it('shows installation, AI reader and image information', async () => {
    await setup();

    expect(text()).toContain('C:/dados/mercado-no-controle.db');
    expect(text()).toContain('2,5 MB');
    expect(text()).toContain('12 compra(s) confirmada(s)');
    expect(text()).toContain('Leitura automática indisponível');
    expect(text()).toContain('A leitura automática não está configurada.');
    expect(text()).toContain('receipts/2026/11/a.jpg');
    expect(text()).toContain('órfãs · 1,9 MB');
    expect(text()).toContain('Protegido por login');
  });

  it('hides the account section without authentication', async () => {
    await setup(false);

    expect(text()).not.toContain('Alterar senha');
  });

  it('saves display preferences', async () => {
    await setup();

    page().setPageSize(50);
    page().setPeriod('year');

    expect(TestBed.inject(PreferencesService).preferences()).toEqual({ pageSize: 50, analyticsPeriod: 'year' });
    expect(notification.success).toHaveBeenCalledTimes(2);
  });

  it('deletes orphan images after confirmation and reloads the summary', async () => {
    await setup();

    confirm.mockReturnValue(of(false));
    page().deleteOrphans();
    http.expectNone('/api/settings/images/orphans');

    confirm.mockReturnValue(of(true));
    page().deleteOrphans();
    http.expectOne('/api/settings/images/orphans').flush({ deletedFiles: 2, freedBytes: 2_000_000 });
    await settle();
    http.expectOne('/api/settings/images').flush({ ...images, totalFiles: 1, orphans: [], orphanBytes: 0 });
    await settle();

    expect(notification.success).toHaveBeenCalledWith('2 imagem(ns) excluída(s), 1,9 MB liberados.');
    expect(text()).toContain('Nenhuma imagem órfã.');
  });

  it('downloads the backup', async () => {
    const createObjectURL = vi.fn(() => 'blob:x');
    Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() });
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await setup();

    page().downloadBackup();
    expect(page().downloading()).toBe('backup');
    http.expectOne('/api/settings/backup').flush(new Blob(['zip']), {
      headers: { 'Content-Disposition': 'attachment; filename="mercado-no-controle-backup-20261123-1000.zip"' },
    });

    expect(click).toHaveBeenCalled();
    expect(page().downloading()).toBeNull();
    expect(notification.success).toHaveBeenCalledWith('Backup gerado.');
    click.mockRestore();
  });

  it('changes the password and maps the wrong current password to its field', async () => {
    await setup();
    auth.changePassword.mockReturnValue(of(undefined));

    page().passwordForm.setValue({ currentPassword: 'antiga-123', password: 'nova-12345', confirmPassword: 'nova-12345' });
    page().changePassword();

    expect(auth.changePassword).toHaveBeenCalledWith('antiga-123', 'nova-12345');
    expect(notification.success).toHaveBeenCalledWith('Senha alterada. Outras sessões abertas foram encerradas.');
    expect(page().passwordForm.getRawValue().currentPassword).toBe('');
  });

  it('prefills and saves the profile', async () => {
    await setup();
    expect(page().profileForm.getRawValue()).toEqual({ name: 'Pessoa', email: 'eu@example.com' });
    auth.updateProfile.mockReturnValue(of({ id: 1, name: 'Novo', email: 'novo@example.com', lastLoginAt: null }));

    page().profileForm.setValue({ name: 'Novo', email: 'novo@example.com' });
    page().profileForm.markAsDirty();
    page().saveProfile();

    expect(auth.updateProfile).toHaveBeenCalledWith('Novo', 'novo@example.com');
    expect(notification.success).toHaveBeenCalledWith('Dados da conta atualizados.');
  });
});
