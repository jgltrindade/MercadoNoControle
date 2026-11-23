import { Pipe, PipeTransform } from '@angular/core';

/** Formata um CNPJ normalizado (14 caracteres) como XX.XXX.XXX/XXXX-XX. */
export function formatCnpj(value: string | null | undefined): string {
  if (!value) {
    return '';
  }
  if (value.length !== 14) {
    return value;
  }
  return `${value.slice(0, 2)}.${value.slice(2, 5)}.${value.slice(5, 8)}/${value.slice(8, 12)}-${value.slice(12)}`;
}

@Pipe({ name: 'cnpj' })
export class CnpjPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return formatCnpj(value);
  }
}
