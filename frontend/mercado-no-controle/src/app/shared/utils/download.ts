import { HttpResponse } from '@angular/common/http';

/** Arquivo baixado da API, com o nome definido pelo servidor. */
export interface DownloadedFile {
  blob: Blob;
  fileName: string;
}

/** Nome do arquivo no cabeçalho Content-Disposition (`filename*` UTF-8 tem prioridade). */
export function fileNameFromResponse(response: HttpResponse<Blob>, fallback: string): string {
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  if (encoded) {
    return decodeURIComponent(encoded[1]);
  }
  const plain = /filename="?([^";]+)"?/i.exec(disposition);
  return plain ? plain[1] : fallback;
}

export function toDownloadedFile(response: HttpResponse<Blob>, fallback: string): DownloadedFile {
  return { blob: response.body!, fileName: fileNameFromResponse(response, fallback) };
}

/** Dispara o download de um arquivo no navegador. */
export function saveFile(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
