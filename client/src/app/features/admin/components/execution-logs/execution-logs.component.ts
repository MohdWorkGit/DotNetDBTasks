import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { ExecutionLog } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-execution-logs',
  template: `
    <div class="container">
      <h2>Execution Logs</h2>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <div *ngIf="!loading && errorMessage" class="error-block">
            <p class="error-text">{{ errorMessage }}</p>
            <button mat-raised-button color="primary" (click)="loadLogs()">
              <mat-icon>refresh</mat-icon> Retry
            </button>
          </div>

          <div *ngIf="!loading && !errorMessage" class="table-toolbar">
            <mat-form-field appearance="outline" class="filter-field">
              <mat-label>Filter logs</mat-label>
              <input matInput (keyup)="applyFilter($event)" placeholder="Search by query, user, parameters, error...">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>

            <mat-form-field appearance="outline" class="status-filter">
              <mat-label>Status</mat-label>
              <mat-select [(value)]="statusFilter" (selectionChange)="refreshFilter()">
                <mat-option value="all">All</mat-option>
                <mat-option value="success">Success</mat-option>
                <mat-option value="failed">Failed</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <table mat-table [dataSource]="dataSource" matSort matSortActive="executedAt" matSortDirection="desc"
                 *ngIf="!loading && !errorMessage">
            <ng-container matColumnDef="queryName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Query</th>
              <td mat-cell *matCellDef="let log">{{ log.queryName }}</td>
            </ng-container>

            <ng-container matColumnDef="username">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>User</th>
              <td mat-cell *matCellDef="let log">{{ log.username }}</td>
            </ng-container>

            <ng-container matColumnDef="parameters">
              <th mat-header-cell *matHeaderCellDef>Parameters</th>
              <td mat-cell *matCellDef="let log">
                <ng-container *ngIf="hasOldRows(log); else plainParams">
                  <div class="update-params">
                    <span class="update-label old-label">
                      Before ({{ log.oldValues.length }} row{{ log.oldValues.length === 1 ? '' : 's' }}):
                    </span>
                    <span class="parameters-cell old-values" [matTooltip]="formatRowsTooltip(log.oldValues)">
                      {{ formatRowsSummary(log.oldValues) }}
                    </span>
                    <ng-container *ngIf="log.isUpdateQuery">
                      <span class="update-label new-label">After:</span>
                      <span class="parameters-cell new-values" [matTooltip]="formatParametersTooltip(log.parameters)">
                        {{ formatParameters(log.parameters) }}
                      </span>
                    </ng-container>
                  </div>
                </ng-container>
                <ng-template #plainParams>
                  <span class="parameters-cell" [matTooltip]="formatParametersTooltip(log.parameters)">
                    {{ formatParameters(log.parameters) }}
                  </span>
                </ng-template>
              </td>
            </ng-container>

            <ng-container matColumnDef="executedAt">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Executed At</th>
              <td mat-cell *matCellDef="let log">{{ log.executedAt | date:'medium' }}</td>
            </ng-container>

            <ng-container matColumnDef="executionDurationMs">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Duration (ms)</th>
              <td mat-cell *matCellDef="let log">{{ log.executionDurationMs }}</td>
            </ng-container>

            <ng-container matColumnDef="rowsReturned">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Rows</th>
              <td mat-cell *matCellDef="let log">{{ log.rowsReturned }}</td>
            </ng-container>

            <ng-container matColumnDef="isSuccess">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
              <td mat-cell *matCellDef="let log">
                <mat-icon [class]="log.isSuccess ? 'success' : 'error'"
                          [matTooltip]="log.isSuccess ? 'Success' : (log.errorMessage || 'Unknown error')"
                          [matTooltipClass]="log.isSuccess ? 'success-tooltip' : 'error-tooltip'">
                  {{ log.isSuccess ? 'check_circle' : 'error' }}
                </mat-icon>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>

            <tr class="mat-row no-data-row" *matNoDataRow>
              <td class="mat-cell no-data-cell" [attr.colspan]="displayedColumns.length">
                No logs match the current filters.
              </td>
            </tr>
          </table>

          <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons>
          </mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .loading { display: flex; justify-content: center; padding: 40px; }
    .error-block { text-align: center; padding: 24px; }
    .error-text { color: var(--status-error); margin-bottom: 16px; }
    .success { color: var(--status-success); cursor: default; }
    .error { color: var(--status-error); cursor: help; }
    .table-toolbar { display: flex; gap: 12px; align-items: flex-start; margin-bottom: 8px; }
    .filter-field { flex: 1; min-width: 240px; }
    .status-filter { width: 160px; }
    .no-data-row { height: 56px; }
    .no-data-cell { text-align: center; color: var(--text-secondary); padding: 16px; }
    table { width: 100%; }
    .parameters-cell {
      max-width: 250px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      display: block;
      font-size: 12px;
      color: var(--param-cell-color);
      cursor: default;
    }
    .update-params {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .update-label {
      font-size: 10px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }
    .old-label { color: var(--old-label-color); }
    .new-label { color: var(--new-label-color); }
    .old-values { color: var(--old-values-color); }
    .new-values { color: var(--new-values-color); }
  `]
})
export class ExecutionLogsComponent implements OnInit {
  displayedColumns = ['queryName', 'username', 'parameters', 'executedAt', 'executionDurationMs', 'rowsReturned', 'isSuccess'];
  dataSource = new MatTableDataSource<ExecutionLog>();
  loading = true;
  errorMessage = '';
  statusFilter: 'all' | 'success' | 'failed' = 'all';
  private textFilter = '';

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private queryService: QueryService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.loadLogs();
  }

  loadLogs(): void {
    this.loading = true;
    this.errorMessage = '';
    this.queryService.getExecutionLogs().pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out. Please try again.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (logs) => {
        this.dataSource.data = logs;
        this.dataSource.paginator = this.paginator;
        this.dataSource.sort = this.sort;
        this.dataSource.sortingDataAccessor = (item: ExecutionLog, property: string) => {
          switch (property) {
            case 'queryName': return (item.queryName || '').toLowerCase();
            case 'username': return (item.username || '').toLowerCase();
            case 'executedAt': return new Date(item.executedAt).getTime();
            case 'isSuccess': return item.isSuccess ? 1 : 0;
            default: return (item as any)[property];
          }
        };
        this.dataSource.filterPredicate = (data: ExecutionLog, filter: string) => {
          const f = JSON.parse(filter) as { text: string; status: 'all' | 'success' | 'failed' };
          if (f.status === 'success' && !data.isSuccess) return false;
          if (f.status === 'failed' && data.isSuccess) return false;
          if (!f.text) return true;
          const paramsStr = data.parameters ? Object.entries(data.parameters).map(([k, v]) => `${k} ${v}`).join(' ') : '';
          const haystack = [
            data.queryName,
            data.username,
            data.errorMessage,
            paramsStr
          ].filter(Boolean).join(' ').toLowerCase();
          return haystack.includes(f.text);
        };
        this.refreshFilter();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || 'Failed to load logs. Please try again.';
        this.cdr.detectChanges();
      }
    });
  }

  applyFilter(event: Event): void {
    this.textFilter = (event.target as HTMLInputElement).value.trim().toLowerCase();
    this.refreshFilter();
  }

  refreshFilter(): void {
    this.dataSource.filter = JSON.stringify({ text: this.textFilter, status: this.statusFilter });
    if (this.dataSource.paginator) this.dataSource.paginator.firstPage();
  }

  formatParameters(params: Record<string, string>): string {
    if (!params || Object.keys(params).length === 0) return '-';
    return Object.entries(params).map(([k, v]) => `${k}: ${v}`).join(', ');
  }

  formatParametersTooltip(params: Record<string, string>): string {
    if (!params || Object.keys(params).length === 0) return 'No parameters';
    return Object.entries(params).map(([k, v]) => `${k}: ${v}`).join('\n');
  }

  hasOldRows(log: ExecutionLog): boolean {
    return !!log.oldValues && Array.isArray(log.oldValues) && log.oldValues.length > 0;
  }

  formatRowsSummary(rows: Record<string, string>[]): string {
    if (!rows || rows.length === 0) return '-';
    const first = this.formatParameters(rows[0]);
    return rows.length === 1 ? first : `${first} (+${rows.length - 1} more)`;
  }

  formatRowsTooltip(rows: Record<string, string>[]): string {
    if (!rows || rows.length === 0) return 'No rows';
    return rows
      .map((r, i) => `Row ${i + 1}:\n${this.formatParametersTooltip(r)}`)
      .join('\n\n');
  }
}
