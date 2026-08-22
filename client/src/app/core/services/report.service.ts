import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import {
  Report,
  ReportAccess,
  ReportInput,
  ReportRun,
  ReportSummary,
  ReportTemplateInspection
} from '@core/models/report.model';

/** The download endpoint's ?format= values, matching the query export contract. */
export type ReportExportFormat = 'xlsx' | 'csv' | 'json' | 'pdf' | 'docx';

/**
 * Everything the report pages talk to.
 *
 * <p>Note what is absent: there is no method for fetching a section's rows. A report run files
 * each section into the same result cache the query grid uses, so the viewer pages sections with
 * <c>QueryService.getJobRows</c> against the section's <c>jobId</c>. Reports therefore inherit
 * the existing server-side filter and sort behaviour instead of growing a second, subtly
 * different copy of it.</p>
 */
@Injectable({ providedIn: 'root' })
export class ReportService {
  private http = inject(HttpClient);

  private adminUrl = `${environment.apiUrl}/admin/reports`;
  private userUrl = `${environment.apiUrl}/user/reports`;

  // ---------------------------------------------------------------- authoring

  getAll(): Observable<ReportSummary[]> {
    return this.http.get<ReportSummary[]>(this.adminUrl);
  }

  getById(id: string): Observable<Report> {
    return this.http.get<Report>(`${this.adminUrl}/${id}`);
  }

  create(input: ReportInput): Observable<Report> {
    return this.http.post<Report>(this.adminUrl, input);
  }

  update(id: string, input: ReportInput): Observable<Report> {
    return this.http.put<Report>(`${this.adminUrl}/${id}`, input);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.adminUrl}/${id}`);
  }

  // ---------------------------------------------------------------- template

  /**
   * Uploads the Word template and returns what it references, so the builder can flag a
   * mistyped dataset key immediately rather than letting it surface as an empty section.
   */
  uploadTemplate(id: string, file: File): Observable<ReportTemplateInspection> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ReportTemplateInspection>(`${this.adminUrl}/${id}/template`, form);
  }

  downloadTemplate(id: string): Observable<Blob> {
    return this.http.get(`${this.adminUrl}/${id}/template`, { responseType: 'blob' });
  }

  /** The generated starter, already carrying this report's dataset keys. */
  downloadStarterTemplate(id: string): Observable<Blob> {
    return this.http.get(`${this.adminUrl}/${id}/starter-template`, { responseType: 'blob' });
  }

  removeTemplate(id: string): Observable<void> {
    return this.http.delete<void>(`${this.adminUrl}/${id}/template`);
  }

  // ---------------------------------------------------------------- access

  getAccess(id: string): Observable<ReportAccess> {
    return this.http.get<ReportAccess>(`${this.adminUrl}/${id}/access`);
  }

  setAccess(id: string, access: ReportAccess): Observable<ReportAccess> {
    return this.http.put<ReportAccess>(`${this.adminUrl}/${id}/access`, access);
  }

  // ---------------------------------------------------------------- running

  getMine(): Observable<ReportSummary[]> {
    return this.http.get<ReportSummary[]>(this.userUrl);
  }

  getMyReport(id: string): Observable<Report> {
    return this.http.get<Report>(`${this.userUrl}/${id}`);
  }

  /**
   * Runs the report. Returns section metadata and one cached-result job id per section — the
   * rows themselves do not cross the wire here.
   */
  run(id: string, parameters: Record<string, string>): Observable<ReportRun> {
    return this.http.post<ReportRun>(`${this.userUrl}/${id}/run`, { parameters });
  }

  getRun(runId: string): Observable<ReportRun> {
    return this.http.get<ReportRun>(`${this.userUrl}/runs/${runId}`);
  }

  /** Downloads the whole run as one document, built from the already-cached sections. */
  exportRun(runId: string, format: ReportExportFormat): Observable<Blob> {
    return this.http.get(`${this.userUrl}/runs/${runId}/export-file`, {
      params: { format },
      responseType: 'blob'
    });
  }

  /**
   * Releases the run and every section's cached rows. Called when leaving the viewer; they
   * would otherwise sit in the cache until the retention window expired them.
   */
  releaseRun(runId: string): Observable<void> {
    return this.http.delete<void>(`${this.userUrl}/runs/${runId}`);
  }
}
