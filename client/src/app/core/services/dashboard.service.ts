import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { DropdownOption } from '@core/models/dynamic-query.model';
import {
  Dashboard,
  DashboardAccess,
  DashboardColumnsProbe,
  DashboardInput,
  DashboardSummary,
  DashboardTileData,
  DashboardTileRows
} from '@core/models/dashboard.model';

/**
 * Everything the dashboard pages talk to.
 *
 * <p>Tile data is fetched one tile at a time, on each tile's own interval, rather than as a whole
 * dashboard: a slow tile then delays only itself. The server shares each result between everyone
 * viewing the dashboard, so polling does not multiply the load on the database.</p>
 */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private http = inject(HttpClient);

  private adminUrl = `${environment.apiUrl}/admin/dashboards`;
  private userUrl = `${environment.apiUrl}/user/dashboards`;

  // ---------------------------------------------------------------- authoring

  getAll(): Observable<DashboardSummary[]> {
    return this.http.get<DashboardSummary[]>(this.adminUrl);
  }

  getById(id: string): Observable<Dashboard> {
    return this.http.get<Dashboard>(`${this.adminUrl}/${id}`);
  }

  create(input: DashboardInput): Observable<Dashboard> {
    return this.http.post<Dashboard>(this.adminUrl, input);
  }

  update(id: string, input: DashboardInput): Observable<Dashboard> {
    return this.http.put<Dashboard>(`${this.adminUrl}/${id}`, input);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.adminUrl}/${id}`);
  }

  /** Runs the query once and returns its columns, for the builder's pickers. */
  probeColumns(queryId: string, parameters: Record<string, string>): Observable<DashboardColumnsProbe> {
    return this.http.post<DashboardColumnsProbe>(`${this.adminUrl}/probe-columns`, { queryId, parameters });
  }

  getAccess(id: string): Observable<DashboardAccess> {
    return this.http.get<DashboardAccess>(`${this.adminUrl}/${id}/access`);
  }

  setAccess(id: string, access: DashboardAccess): Observable<DashboardAccess> {
    return this.http.put<DashboardAccess>(`${this.adminUrl}/${id}/access`, access);
  }

  // ---------------------------------------------------------------- viewing

  getMine(): Observable<DashboardSummary[]> {
    return this.http.get<DashboardSummary[]>(this.userUrl);
  }

  getMyDashboard(id: string): Observable<Dashboard> {
    return this.http.get<Dashboard>(`${this.userUrl}/${id}`);
  }

  /** One tile's current data. Answers 200 with `error` set when only this tile failed. */
  getTileData(id: string, tileId: string, filters: Record<string, string>): Observable<DashboardTileData> {
    return this.http.post<DashboardTileData>(`${this.userUrl}/${id}/tiles/${tileId}/data`, { filters });
  }

  /** Runs the tile's query in full and returns the job its rows page from. */
  runTileRows(id: string, tileId: string, filters: Record<string, string>): Observable<DashboardTileRows> {
    return this.http.post<DashboardTileRows>(`${this.userUrl}/${id}/tiles/${tileId}/rows`, { filters });
  }

  getFilterOptions(id: string, filterId: string): Observable<DropdownOption[]> {
    return this.http.get<DropdownOption[]>(`${this.userUrl}/${id}/filters/${filterId}/options`);
  }
}
