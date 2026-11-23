import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';
import { ApiError } from '../../core/api/api-error';

/** Mensagem do primeiro erro de um campo, para exibição em `mat-error`. */
export function fieldErrorMessage(control: AbstractControl | null): string | null {
  const errors = control?.errors;
  if (!errors) {
    return null;
  }
  if (errors['server']) return String(errors['server']);
  if (errors['required']) return 'Campo obrigatório.';
  if (errors['maxlength']) return `Máximo de ${errors['maxlength'].requiredLength} caracteres.`;
  if (errors['min']) return `O valor mínimo é ${errors['min'].min}.`;
  if (errors['max']) return `O valor máximo é ${errors['max'].max}.`;
  if (errors['decimalPlaces']) return `Use no máximo ${errors['decimalPlaces'].max} casas decimais.`;
  if (errors['cnpjFormat']) return 'O CNPJ deve ter 14 caracteres (com ou sem máscara).';
  if (errors['digits']) return 'Use somente números.';
  return 'Valor inválido.';
}

/**
 * Aplica os erros por campo retornados pela API (ProblemDetails.errors) aos controles do
 * formulário. Retorna `true` se algum erro foi associado a um campo.
 */
export function applyServerErrors(form: FormGroup, error: ApiError): boolean {
  let applied = false;
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const control = form.get(field);
    if (control && messages.length > 0) {
      control.setErrors({ ...control.errors, server: messages.join(' ') });
      control.markAsTouched();
      applied = true;
    }
  }
  return applied;
}

/** Limita a quantidade de casas decimais de um número. */
export function maxDecimalPlaces(max: number): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = control.value;
    if (value === null || value === undefined || value === '') {
      return null;
    }
    const [, decimals = ''] = String(value).split('.');
    return decimals.length > max ? { decimalPlaces: { max } } : null;
  };
}

/**
 * Verifica apenas o formato do CNPJ (14 caracteres alfanuméricos após remover a máscara).
 * Os dígitos verificadores são validados pelo backend.
 */
export function cnpjFormat(control: AbstractControl): ValidationErrors | null {
  const value = (control.value ?? '').toString().trim();
  if (!value) {
    return null;
  }
  return /^[0-9A-Za-z]{12}[0-9]{2}$/.test(value.replace(/[./\-\s]/g, '')) ? null : { cnpjFormat: true };
}

/** Somente dígitos. */
export function digitsOnly(control: AbstractControl): ValidationErrors | null {
  const value = (control.value ?? '').toString().trim();
  return !value || /^\d+$/.test(value) ? null : { digits: true };
}

/** Converte string vazia em `null` para envio à API. */
export function emptyToNull(value: string | null | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}
