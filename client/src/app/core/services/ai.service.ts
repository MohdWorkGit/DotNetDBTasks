import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap, timeout } from 'rxjs/operators';
import { environment } from '@env/environment';
import {
  AiDashboardDraft, AiSqlExplanation, AiSqlFix, AiStatus, DraftDashboardRequest, ExplainSqlRequest,
  SuggestSqlFixRequest
} from '@core/models/ai.model';

/**
 * The local AI model (docs/LOCAL-AI-OPTIONS.md). A request can take a minute or more on a
 * server without a GPU, so calls wait as long as the server itself will (its timeout, learned
 * from the status), plus a margin — not the 30 seconds other calls get.
 */
@Injectable({ providedIn: 'root' })
export class AiService {
  private readonly url = `${environment.apiUrl}/admin/ai`;
  private serverTimeoutSeconds = 180;

  constructor(private http: HttpClient) {}

  getStatus(): Observable<AiStatus> {
    return this.http.get<AiStatus>(`${this.url}/status`).pipe(
      tap(status => { if (status.timeoutSeconds > 0) this.serverTimeoutSeconds = status.timeoutSeconds; })
    );
  }

  explainSql(request: ExplainSqlRequest): Observable<AiSqlExplanation> {
    return this.http.post<AiSqlExplanation>(`${this.url}/explain-sql`, request).pipe(timeout(this.waitMs()));
  }

  suggestFix(request: SuggestSqlFixRequest): Observable<AiSqlFix> {
    return this.http.post<AiSqlFix>(`${this.url}/suggest-fix`, request).pipe(timeout(this.waitMs()));
  }

  /** Runs each picked query once to learn its columns, then asks the model: the slowest call here. */
  draftDashboard(request: DraftDashboardRequest): Observable<AiDashboardDraft> {
    return this.http.post<AiDashboardDraft>(`${this.url}/draft-dashboard`, request).pipe(timeout(this.waitMs()));
  }

  private waitMs(): number {
    return (this.serverTimeoutSeconds + 15) * 1000;
  }
}
