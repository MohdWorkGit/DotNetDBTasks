import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { TransferImportResult, TransferKind } from '@core/models/transfer.model';

/**
 * Downloads report and dashboard definitions as portable JSON and imports such files back.
 * The query list keeps its own endpoints; the import here also reads a query backup.
 */
@Injectable({ providedIn: 'root' })
export class TransferService {
  private http = inject(HttpClient);
  private adminUrl = `${environment.apiUrl}/admin`;

  /** One item, or every item of the kind when <c>id</c> is null. */
  export(kind: TransferKind, id: string | null, includeDependencies: boolean): Observable<Blob> {
    const base = `${this.adminUrl}/${kind === 'report' ? 'reports' : 'dashboards'}`;
    const params = new HttpParams().set('includeDependencies', String(includeDependencies));
    return this.http.get(id ? `${base}/${id}/export` : `${base}/export`, { params, responseType: 'blob' });
  }

  /** Never overwrites: a name clash comes in as "Name (imported)". */
  import(file: File): Observable<TransferImportResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<TransferImportResult>(`${this.adminUrl}/transfer/import`, form);
  }

  /** Hands a downloaded file to the browser. */
  save(blob: Blob, fileName: string): void {
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  /** Mirrors the server's file-name slug, so a download lands with a sane name. */
  slug(name: string, fallback: string): string {
    const cleaned = name.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-+|-+$/g, '');
    return cleaned || fallback;
  }
}
