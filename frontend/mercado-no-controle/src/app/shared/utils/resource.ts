import { Resource, Signal, linkedSignal } from '@angular/core';

/**
 * Último valor carregado de um resource. Enquanto um novo carregamento está em andamento,
 * mantém o valor anterior (evita que tabelas "pisquem" ao paginar, ordenar ou filtrar).
 * Em caso de erro, retorna `undefined`.
 */
export function latestValue<T>(resource: Resource<T | undefined>): Signal<T | undefined> {
  return linkedSignal<{ value: T | undefined; loading: boolean }, T | undefined>({
    source: () => ({
      value: resource.hasValue() ? resource.value() : undefined,
      loading: resource.isLoading(),
    }),
    computation: (current, previous) => current.value ?? (current.loading ? previous?.value : undefined),
  });
}
