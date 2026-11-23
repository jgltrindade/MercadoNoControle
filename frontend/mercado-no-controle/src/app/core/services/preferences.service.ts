import { Injectable, signal } from '@angular/core';
import { DEFAULT_PAGE_SIZE, PAGE_SIZE_OPTIONS } from '../../shared/models/paging';

export interface DisplayPreferences {
  /** Período inicial do dashboard e dos relatórios (valor de `PERIOD_PRESETS`). */
  analyticsPeriod: string;
  /** Itens por página nas listagens. */
  pageSize: number;
}

export const DEFAULT_PREFERENCES: DisplayPreferences = { analyticsPeriod: 'last12', pageSize: DEFAULT_PAGE_SIZE };

const STORAGE_KEY = 'mnc.preferences';

/**
 * Preferências de exibição deste navegador. Ficam no armazenamento local (não são dados do
 * usuário na API): se o armazenamento estiver indisponível, valem os padrões.
 */
@Injectable({ providedIn: 'root' })
export class PreferencesService {
  private readonly state = signal<DisplayPreferences>(read());

  readonly preferences = this.state.asReadonly();

  update(change: Partial<DisplayPreferences>): void {
    const next = sanitize({ ...this.state(), ...change });
    this.state.set(next);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    } catch {
      // Armazenamento bloqueado (ex.: navegação privada): mantém só nesta sessão.
    }
  }

  reset(): void {
    this.update(DEFAULT_PREFERENCES);
  }
}

function read(): DisplayPreferences {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? sanitize({ ...DEFAULT_PREFERENCES, ...JSON.parse(raw) }) : DEFAULT_PREFERENCES;
  } catch {
    return DEFAULT_PREFERENCES;
  }
}

function sanitize(value: DisplayPreferences): DisplayPreferences {
  return {
    analyticsPeriod: typeof value.analyticsPeriod === 'string' ? value.analyticsPeriod : DEFAULT_PREFERENCES.analyticsPeriod,
    pageSize: PAGE_SIZE_OPTIONS.includes(Number(value.pageSize)) ? Number(value.pageSize) : DEFAULT_PREFERENCES.pageSize,
  };
}
