import { HttpErrorResponse } from '@angular/common/http';

/** Formato padronizado de erro retornado pela API (ProblemDetails, RFC 9457). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

/** Erro normalizado para exibição na interface. */
export interface ApiError {
  status: number;
  message: string;
  fieldErrors: Record<string, string[]>;
}

const CONNECTION_ERROR_MESSAGE = 'Não foi possível conectar ao servidor. Verifique se a API está em execução.';
const GENERIC_ERROR_MESSAGE = 'Ocorreu um erro inesperado. Tente novamente.';

/** Converte qualquer erro HTTP em uma mensagem amigável e erros por campo. */
export function toApiError(error: unknown): ApiError {
  // Resources (rxResource) envolvem erros que não são `Error` em um wrapper com `cause`.
  if (error instanceof Error && error.cause instanceof HttpErrorResponse) {
    error = error.cause;
  }

  if (!(error instanceof HttpErrorResponse)) {
    return { status: 0, message: GENERIC_ERROR_MESSAGE, fieldErrors: {} };
  }

  if (error.status === 0) {
    return { status: 0, message: CONNECTION_ERROR_MESSAGE, fieldErrors: {} };
  }

  const problem = isProblemDetails(error.error) ? error.error : undefined;
  return {
    status: error.status,
    message: problem?.detail ?? problem?.title ?? GENERIC_ERROR_MESSAGE,
    fieldErrors: problem?.errors ?? {},
  };
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && ('title' in value || 'detail' in value);
}
