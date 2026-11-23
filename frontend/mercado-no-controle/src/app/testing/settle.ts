import { TestBed } from '@angular/core/testing';

/**
 * Executa efeitos e detecção de mudanças e aguarda as microtarefas (resources usam promises).
 * Diferente de `fixture.whenStable()`, não espera requisições HTTP pendentes — necessário em
 * testes com HttpTestingController, onde a requisição só termina quando o teste chama `flush`.
 */
export async function settle(): Promise<void> {
  TestBed.tick();
  await new Promise((resolve) => setTimeout(resolve, 0));
  TestBed.tick();
}
