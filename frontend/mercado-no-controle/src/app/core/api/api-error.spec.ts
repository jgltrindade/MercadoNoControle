import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from './api-error';

describe('toApiError', () => {
  it('reports connection failures when the API is unreachable', () => {
    const error = toApiError(new HttpErrorResponse({ status: 0 }));

    expect(error.status).toBe(0);
    expect(error.message).toContain('Não foi possível conectar');
  });

  it('uses the ProblemDetails detail and field errors', () => {
    const error = toApiError(
      new HttpErrorResponse({
        status: 422,
        error: {
          title: 'Regra de negócio violada',
          detail: 'O total da nota não confere.',
          errors: { total: ['Valor divergente.'] },
        },
      }),
    );

    expect(error).toEqual({
      status: 422,
      message: 'O total da nota não confere.',
      fieldErrors: { total: ['Valor divergente.'] },
    });
  });

  it('falls back to the ProblemDetails title', () => {
    const error = toApiError(new HttpErrorResponse({ status: 404, error: { title: 'Recurso não encontrado' } }));

    expect(error.message).toBe('Recurso não encontrado');
  });

  it('uses a generic message for unknown errors', () => {
    expect(toApiError(new Error('boom')).message).toBe('Ocorreu um erro inesperado. Tente novamente.');
    expect(toApiError(new HttpErrorResponse({ status: 500, error: 'texto' })).message).toBe(
      'Ocorreu um erro inesperado. Tente novamente.',
    );
  });
});
