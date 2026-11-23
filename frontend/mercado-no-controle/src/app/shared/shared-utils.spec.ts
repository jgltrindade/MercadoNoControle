import { PtBrPaginatorIntl } from '../core/i18n/paginator-intl';
import { formatPackage } from '../features/products/products.service';
import { toHttpParams } from './models/paging';
import { formatCnpj } from './pipes/cnpj.pipe';

describe('shared utilities', () => {
  it('formats normalized CNPJs', () => {
    expect(formatCnpj('11222333000181')).toBe('11.222.333/0001-81');
    expect(formatCnpj('12ABC34501DE35')).toBe('12.ABC.345/01DE-35');
    expect(formatCnpj(null)).toBe('');
    expect(formatCnpj('123')).toBe('123');
  });

  it('builds query params skipping empty values', () => {
    const params = toHttpParams({ page: 1, search: '', state: null, categoryId: undefined, sortBy: 'name' });

    expect(params.toString()).toBe('page=1&sortBy=name');
  });

  it('formats package content in pt-BR', () => {
    expect(formatPackage(1, 'L')).toBe('1 L');
    expect(formatPackage(0.5, 'KG')).toBe('0,5 KG');
    expect(formatPackage(null, 'UN')).toBe('UN');
  });

  it('translates the paginator range label', () => {
    const intl = new PtBrPaginatorIntl();

    expect(intl.itemsPerPageLabel).toBe('Itens por página');
    expect(intl.getRangeLabel(1, 20, 45)).toBe('21 – 40 de 45');
    expect(intl.getRangeLabel(0, 20, 0)).toBe('0 de 0');
  });
});
