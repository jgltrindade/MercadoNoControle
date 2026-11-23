import { FormControl, FormGroup, Validators } from '@angular/forms';
import { applyServerErrors, cnpjFormat, digitsOnly, emptyToNull, fieldErrorMessage, maxDecimalPlaces } from './form-errors';

describe('form-errors', () => {
  it('describes the first validation error of a control', () => {
    expect(fieldErrorMessage(new FormControl('', Validators.required))).toBe('Campo obrigatório.');
    expect(fieldErrorMessage(new FormControl('abcd', Validators.maxLength(3)))).toBe('Máximo de 3 caracteres.');
    expect(fieldErrorMessage(new FormControl(1.23456, maxDecimalPlaces(4)))).toBe('Use no máximo 4 casas decimais.');
    expect(fieldErrorMessage(new FormControl('ok'))).toBeNull();
    expect(fieldErrorMessage(null)).toBeNull();
  });

  it('prefers the server message', () => {
    const control = new FormControl('x');
    control.setErrors({ server: 'CNPJ inválido.', required: true });

    expect(fieldErrorMessage(control)).toBe('CNPJ inválido.');
  });

  it('applies server field errors to matching controls', () => {
    const form = new FormGroup({ cnpj: new FormControl('1'), name: new FormControl('a') });

    const applied = applyServerErrors(form, {
      status: 400,
      message: 'Erro',
      fieldErrors: { cnpj: ['CNPJ inválido.'], unknown: ['ignorado'] },
    });

    expect(applied).toBe(true);
    expect(form.controls.cnpj.errors).toEqual({ server: 'CNPJ inválido.' });
    expect(form.controls.cnpj.touched).toBe(true);
    expect(form.controls.name.errors).toBeNull();
  });

  it('reports when no field error could be applied', () => {
    const form = new FormGroup({ name: new FormControl('a') });

    expect(applyServerErrors(form, { status: 500, message: 'Erro', fieldErrors: {} })).toBe(false);
  });

  it.each([
    [null, true],
    ['', true],
    [1, true],
    [1.5, true],
    [0.0001, true],
    [1.12345, false],
  ])('maxDecimalPlaces(4) for %s is valid: %s', (value, valid) => {
    expect(maxDecimalPlaces(4)(new FormControl(value)) === null).toBe(valid);
  });

  it.each([
    ['', true],
    ['11.222.333/0001-81', true],
    ['11222333000181', true],
    ['12.ABC.345/01DE-35', true],
    ['1122233300018', false],
    ['12ABC34501DEAB', false],
  ])('cnpjFormat accepts only 14-character CNPJs (%s → %s)', (value, valid) => {
    expect(cnpjFormat(new FormControl(value)) === null).toBe(valid);
  });

  it('digitsOnly rejects letters', () => {
    expect(digitsOnly(new FormControl('789123'))).toBeNull();
    expect(digitsOnly(new FormControl('78A'))).toEqual({ digits: true });
  });

  it('emptyToNull trims and converts blank strings', () => {
    expect(emptyToNull('  ')).toBeNull();
    expect(emptyToNull(' a ')).toBe('a');
    expect(emptyToNull(null)).toBeNull();
  });
});
