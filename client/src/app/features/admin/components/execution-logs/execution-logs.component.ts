import { Component, OnDestroy, OnInit, ChangeDetectorRef } from '@angular/core';
import { PageEvent } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { debounceTime, distinctUntilChanged, timeout, catchError } from 'rxjs/operators';
import { Subject, Subscription, throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { ExecutionLog } from '@core/models/dynamic-query.model';
import { OldRowsDialogComponent } from '@shared/components/old-rows-dialog.component';

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
              <input matInput [value]="searchText" (input)="onSearchInput($event)"
                     placeholder="Search by query, user, parameters, error...">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>

            <mat-form-field appearance="outline" class="status-filter">
              <mat-label>Status</mat-label>
              <mat-select [(value)]="statusFilter" (selectionChange)="onFiltersChanged()">
                <mat-option value="all">All</mat-option>
                <mat-option value="success">Success</mat-option>
                <mat-option value="failed">Failed</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <table mat-table [dataSource]="logs" matSort matSortActive="executedAt" matSortDirection="desc"
                 matSortDisableClear (matSortChange)="onSortChange($event)"
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
                <ng-container *ngIf="log.hasOldValues; else plainParams">
                  <div class="update-params">
                    <span class="update-label old-label">Before:</span>
                    <button type="button" class="old-rows-trigger"
                            (click)="openOldRowsDialog(log)"
                            matTooltip="Click to view affected rows">
                      <span class="old-values">View affected rows</span>
                      <mat-icon class="open-icon">open_in_new</mat-icon>
                    </button>
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

          <mat-paginator *ngIf="!loading && !errorMessage"
                         [length]="totalCount"
                         [pageIndex]="pageNumber - 1"
                         [pageSize]="pageSize"
                         [pageSizeOptions]="[10, 25, 50]"
                         showFirstLastButtons
                         (page)="onPage($event)">
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
    .old-rows-trigger {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      max-width: 250px;
      padding: 2px 6px;
      margin: 0;
      background: transparent;
      border: 1px dashed var(--border-color, #e2e8f0);
      border-radius: 4px;
      cursor: pointer;
      font: inherit;
      text-align: left;
      color: inherit;
    }
    .old-rows-trigger:hover {
      background: var(--bg-secondary, #f8fafc);
      border-color: var(--accent-primary, #4f46e5);
    }
    .old-rows-trigger .old-values {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-size: 12px;
    }
    .old-rows-trigger .open-icon {
      font-size: 14px;
      width: 14px;
      height: 14px;
      color: var(--accent-primary, #4f46e5);
      flex-shrink: 0;
    }
  `]
})
export class ExecutionLogsComponent implements OnInit, OnDestroy {
  displayedColumns = ['queryName', 'username', 'parameters', 'executedAt', 'executionDurationMs', 'rowsReturned', 'isSuccess'];
  logs: ExecutionLog[] = [];
  loading = true;
  errorMessage = '';
  statusFilter: 'all' | 'success' | 'failed' = 'all';
  searchText = '';
  totalCount = 0;
  pageNumber = 1;
  pageSize = 25;
  sortBy = 'executedAt';
  sortDescending = true;

  private readonly searchChanged = new Subject<string>();
  private searchSubscription?: Subscription;

  constructor(
    private queryService: QueryService,
    private cdr: ChangeDetectorRef,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.searchSubscription = this.searchChanged.pipe(
      debounceTime(400),
      distinctUntilChanged()
    ).subscribe(() => {
      this.pageNumber = 1;
      this.loadLogs();
    });
    this.loadLogs();
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
  }

  loadLogs(): void {
    this.loading = true;
    this.errorMessage = '';
    this.queryService.getExecutionLogs({
      search: this.searchText || undefined,
      isSuccess: this.statusFilter === 'all' ? undefined : this.statusFilter === 'success',
      sortBy: this.sortBy,
      sortDescending: this.sortDescending,
      pageNumber: this.pageNumber,
      pageSize: this.pageSize
    }).pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out. Please try again.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (page) => {
        this.logs = page.items;
        this.totalCount = page.totalCount;
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

  onSearchInput(event: Event): void {
    this.searchText = (event.target as HTMLInputElement).value.trim();
    this.searchChanged.next(this.searchText);
  }

  onFiltersChanged(): void {
    this.pageNumber = 1;
    this.loadLogs();
  }

  onSortChange(sort: Sort): void {
    this.sortBy = sort.active;
    this.sortDescending = sort.direction !== 'asc';
    this.pageNumber = 1;
    this.loadLogs();
  }

  onPage(event: PageEvent): void {
    this.pageNumber = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.loadLogs();
  }

  formatParameters(params: Record<string, string>): string {
    if (!params || Object.keys(params).length === 0) return '-';
    return Object.entries(params).map(([k, v]) => `${k}: ${v}`).join(', ');
  }

  formatParametersTooltip(params: Record<string, string>): string {
    if (!params || Object.keys(params).length === 0) return 'No parameters';
    return Object.entries(params).map(([k, v]) => `${k}: ${v}`).join('\n');
  }

  openOldRowsDialog(log: ExecutionLog): void {
    if (!log.hasOldValues) return;
    this.dialog.open(OldRowsDialogComponent, {
      data: {
        queryName: log.queryName,
        fetch: (pageNumber: number, pageSize: number) =>
          this.queryService.getExecutionLogOldValues(log.id, pageNumber, pageSize)
      },
      width: '720px',
      maxWidth: '95vw',
      autoFocus: false
    });
  }
}
