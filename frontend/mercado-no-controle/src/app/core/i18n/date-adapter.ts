import { Injectable, Provider } from '@angular/core';
import { DateAdapter, MAT_DATE_FORMATS, MAT_DATE_LOCALE, MAT_NATIVE_DATE_FORMATS, NativeDateAdapter } from '@angular/material/core';

/** Provedores de data do Angular Material em pt-BR (app e testes). */
export function providePtBrDateAdapter(): Provider[] {
  return [
    { provide: DateAdapter, useClass: PtBrDateAdapter },
    { provide: MAT_DATE_FORMATS, useValue: MAT_NATIVE_DATE_FORMATS },
    { provide: MAT_DATE_LOCALE, useValue: 'pt-BR' },
  ];
}

/**
 * Adaptador de datas nativo que interpreta datas digitadas no formato brasileiro (dd/MM/aaaa).
 * O adaptador padrão usa `Date.parse`, que leria "05/11/2026" como 11 de maio.
 */
@Injectable()
export class PtBrDateAdapter extends NativeDateAdapter {
  override parse(value: unknown, parseFormat?: unknown): Date | null {
    if (typeof value === 'string') {
      const match = /^\s*(\d{1,2})\/(\d{1,2})\/(\d{4})\s*$/.exec(value);
      if (match) {
        const [, day, month, year] = match.map(Number);
        const date = new Date(year, month - 1, day);
        const valid = date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day;
        return valid ? date : this.invalid();
      }
      if (value.trim() === '') {
        return null;
      }
    }
    return super.parse(value, parseFormat);
  }
}

/** Formata uma data local como "aaaa-MM-dd" (sem fuso), para a API. */
export function toIsoDate(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Converte "aaaa-MM-dd" (ou "aaaa-MM-ddTHH:mm:ss") em data local, sem conversão de fuso. */
export function fromIsoDate(value: string): Date {
  const [year, month, day] = value.slice(0, 10).split('-').map(Number);
  return new Date(year, month - 1, day);
}
