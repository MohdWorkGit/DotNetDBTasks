import { Component, OnDestroy, OnInit, ChangeDetectorRef } from '@angular/core';
import { PageEvent } from '@angular/material/paginator';
import { Sort, SortDirection } from '@angular/material/sort';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { debounceTime, distinctUntilChanged, timeout, catchError } from 'rxjs/operators';
import { Subject, Subscription, throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import {
  ExecutionLog,
  isWriteQueryType,
  QUERY_TYPE_LABELS,
  QueryType
} from '@core/models/dynamic-query.model';
import { OldRowsDialogComponent } from '@shared/components/old-rows-dialog.component';

/** Order the API applies when no sortBy is sent; also where a cleared header lands. */
const DEFAULT_SORT_BY = 'executedAt';

@Component({
  standalone: false,
  selector: 'app-execution-logs',
  template: `
    <div class="container">
      <h2>{{ 'admin.logs.title' | transloco }}</h2>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <div *ngIf="!loading && errorMessage" class="error-block">
            <p class="error-text">{{ errorMessage }}</p>
            <button mat-raised-button color="primary" (click)="loadLogs()">
              <mat-icon>refresh</mat-icon> {{ 'common.retry' | transloco }}
            </button>
          </div>

          <div *ngIf="!loading && !errorMessage" class="table-toolbar">
            <mat-form-field appearance="outline" class="filter-field">
              <mat-label>{{ 'admin.logs.filter' | transloco }}</mat-label>
              <input matInput [value]="searchText" (input)="onSearchInput($event)"
                     [attr.placeholder]="'admin.logs.filterPlaceholder' | transloco">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>

            <mat-form-field appearance="outline" class="status-filter">
              <mat-label>{{ 'admin.logs.status' | transloco }}</mat-label>
              <mat-select [(value)]="statusFilter" (selectionChange)="onFiltersChanged()">
                <mat-option value="all">{{ 'common.all' | transloco }}</mat-option>
                <mat-option value="success">{{ 'admin.logs.statusSuccess' | transloco }}</mat-option>
                <mat-option value="failed">{{ 'admin.logs.statusFailed' | transloco }}</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline" class="status-filter">
              <mat-label>{{ 'admin.logs.type' | transloco }}</mat-label>
              <mat-select [(value)]="typeFilter" (selectionChange)="onFiltersChanged()">
                <mat-option value="all">{{ 'common.all' | transloco }}</mat-option>
                <mat-option [value]="QueryType.Select">SELECT</mat-option>
                <mat-option [value]="QueryType.Insert">INSERT</mat-option>
                <mat-option [value]="QueryType.Update">UPDATE</mat-option>
                <mat-option [value]="QueryType.Delete">DELETE</mat-option>
                <mat-option [value]="QueryType.Other">{{ 'admin.logs.typeOther' | transloco }}</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <div class="table-wrapper">
          <table mat-table [dataSource]="logs" matSort
                 [matSortActive]="sortActive" [matSortDirection]="sortDirection"
                 (matSortChange)="onSortChange($event)"
                 *ngIf="!loading && !errorMessage">
            <ng-container matColumnDef="queryName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.logs.query' | transloco }}</th>
              <td mat-cell *matCellDef="let log">{{ log.queryName }}</td>
            </ng-container>

            <ng-container matColumnDef="queryType">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.logs.type' | transloco }}</th>
              <td mat-cell *matCellDef="let log">
                <span class="type-chip" [class.type-write]="isWriteType(log.queryType)">
                  {{ typeLabel(log.queryType) }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="username">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.logs.user' | transloco }}</th>
              <td mat-cell *matCellDef="let log">{{ log.username }}</td>
            </ng-container>

            <ng-container matColumnDef="parameters">
              <th mat-header-cell *matHeaderCellDef>{{ 'admin.logs.parameters' | transloco }}</th>
              <td mat-cell *matCellDef="let log">
                <ng-container *ngIf="log.hasOldValues; else plainParams">
                  <div class="update-params">
                    <span class="update-label old-label">{{ 'admin.logs.before' | transloco }}</span>
                    <button type="button" class="old-rows-trigger"
                            (click)="openOldRowsDialog(log)"
                            [matTooltip]="'admin.logs.viewAffectedRows' | transloco">
                      <span class="old-values">{{ 'common.viewAffectedRows' | transloco }}</span>
                      <mat-icon class="open-icon">open_in_new</mat-icon>
                    </button>
                    <ng-container *ngIf="log.isUpdateQuery">
                      <span class="update-label new-label">{{ 'admin.logs.after' | transloco }}</span>
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
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.logs.executedAt' | transloco }}</th>
              <td mat-cell *matCellDef="let log">{{ log.executedAt | date:'medium' }}</td>
            </ng-container>

            <ng-container matColumnDef="executionDurationMs">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.logs.durationMs' | transloco }}</th>
              <td mat-cell *matCellDef="let log">{{ log.executionDurationMs }}</td>
            </ng-container>

            <ng-container matColumnDef="rowsReturned">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.logs.rows' | transloco }}</th>
              <td mat-cell *matCellDef="let log">{{ log.rowsReturned }}</td>
            </ng-container>

            <ng-container matColumnDef="isSuccess">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.logs.status' | transloco }}</th>
              <td mat-cell *matCellDef="let log">
                <mat-icon [class]="log.isSuccess ? 'success' : 'error'"
                          [matTooltip]="log.isSuccess ? ('admin.logs.statusSuccess' | transloco)
                                       : (log.errorMessage || ('common.unknownError' | transloco))"
                          [matTooltipClass]="log.isSuccess ? 'success-tooltip' : 'error-tooltip'"
                          [attr.aria-label]="log.isSuccess ? ('admin.logs.statusSuccess' | transloco)
                                            : (('admin.logs.statusFailed' | transloco) + ': '
                                               + (log.errorMessage || ('common.unknownError' | transloco)))"
                          role="img">
                  {{ log.isSuccess ? 'check_circle' : 'error' }}
                </mat-icon>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>

            <tr class="mat-row no-data-row" *matNoDataRow>
              <td class="mat-cell no-data-cell" [attr.colspan]="displayedColumns.length">
                {{ 'admin.logs.noMatch' | transloco }}
              </td>
            </tr>
          </table>
          </div>

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
    .success { color: var(--status-success); cursor: default; }
    .error { color: var(--status-error); cursor: help; }
    .filter-field { flex: 1; min-width: 240px; }
    .status-filter { width: 160px; }
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
      border: 1px dashed var(--border-color);
      border-radius: 4px;
      cursor: pointer;
      font: inherit;
      text-align: start;
      color: inherit;
    }
    .old-rows-trigger:hover {
      background: var(--bg-secondary);
      border-color: var(--accent-primary);
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
      color: var(--accent-primary);
      flex-shrink: 0;
    }
  `]
})
export class ExecutionLogsComponent implements OnInit, OnDestroy {
  displayedColumns = ['queryName', 'queryType', 'username', 'parameters', 'executedAt', 'executionDurationMs', 'rowsReturned', 'isSuccess'];
  logs: ExecutionLog[] = [];
  loading = true;
  errorMessage = '';
  statusFilter: 'all' | 'success' | 'failed' = 'all';
  typeFilter: QueryType | 'all' = 'all';
  /// Exposed for the template's mat-option values.
  readonly QueryType = QueryType;
  searchText = '';
  totalCount = 0;
  pageNumber = 1;
  pageSize = 25;
  /** Header state. Mirrored into the component because the table is inside an
   *  *ngIf and is destroyed on every load — MatSort's own state does not survive. */
  sortActive = DEFAULT_SORT_BY;
  sortDirection: SortDirection = 'desc';
  sortBy = DEFAULT_SORT_BY;
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
      queryType: this.typeFilter === 'all' ? undefined : this.typeFilter,
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

  /**
   * Headers cycle asc -> desc -> unsorted. Clearing falls back to the default
   * newest-first order, which is what the API applies when sortBy is omitted.
   */
  isWriteType(type: QueryType): boolean {
    return isWriteQueryType(type);
  }

  typeLabel(type: QueryType): string {
    return QUERY_TYPE_LABELS[type] ?? QUERY_TYPE_LABELS[QueryType.Other];
  }

  onSortChange(sort: Sort): void {
    this.sortActive = sort.active;
    this.sortDirection = sort.direction;
    this.sortBy = sort.direction ? sort.active : DEFAULT_SORT_BY;
    this.sortDescending = sort.direction ? sort.direction === 'desc' : true;
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
      // autoFocus was false, so keyboard focus never entered the dialog (WCAG 2.4.3).
      autoFocus: 'dialog',
      ariaModal: true
    });
  }
}
