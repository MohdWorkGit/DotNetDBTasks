import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { PageEvent } from '@angular/material/paginator';
import { Sort, SortDirection } from '@angular/material/sort';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { ExecutionLog } from '@core/models/dynamic-query.model';
import { OldRowsDialogComponent } from '@shared/components/old-rows-dialog.component';
import { ExecutionLogDetailDialogComponent } from '@shared/components/execution-log-detail-dialog.component';
import { TranslocoService } from '@jsverse/transloco';

/** Order the API applies when no sortBy is sent; also where a cleared header lands. */
const DEFAULT_SORT_BY = 'executedAt';

@Component({
  standalone: false,
  selector: 'app-execution-history',
  template: `
    <div class="container">
      <h2>{{ 'user.history.title' | transloco }}</h2>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <div *ngIf="!loading && errorMessage" class="error-block">
            <p class="error-text">{{ errorMessage }}</p>
            <button mat-raised-button color="primary" (click)="loadHistory()">
              <mat-icon>refresh</mat-icon> {{ 'common.retry' | transloco }}
            </button>
          </div>

          <table mat-table [dataSource]="logs" matSort
                 [matSortActive]="sortActive" [matSortDirection]="sortDirection"
                 (matSortChange)="onSortChange($event)"
                 *ngIf="!loading && !errorMessage">
            <ng-container matColumnDef="queryName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'user.history.query' | transloco }}</th>
              <td mat-cell *matCellDef="let log">{{ log.queryName }}</td>
            </ng-container>

            <ng-container matColumnDef="parameters">
              <th mat-header-cell *matHeaderCellDef>{{ 'user.history.parameters' | transloco }}</th>
              <td mat-cell *matCellDef="let log">
                <div class="params-col">
                  <app-log-params-cell [params]="log.parameters"></app-log-params-cell>
                  <button *ngIf="log.hasOldValues" type="button" class="old-rows-trigger"
                          (click)="openOldRowsDialog(log)"
                          [matTooltip]="'user.history.viewAffectedRows' | transloco">
                    <mat-icon class="open-icon" aria-hidden="true">history</mat-icon>
                    <span>{{ 'admin.logs.beforeRows' | transloco }}</span>
                  </button>
                </div>
              </td>
            </ng-container>

            <ng-container matColumnDef="executedAt">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'user.history.executedAt' | transloco }}</th>
              <td mat-cell *matCellDef="let log">{{ log.executedAt | date:'medium' }}</td>
            </ng-container>

            <ng-container matColumnDef="executionDurationMs">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'user.history.durationMs' | transloco }}</th>
              <td mat-cell *matCellDef="let log">{{ log.executionDurationMs }}</td>
            </ng-container>

            <ng-container matColumnDef="rowsReturned">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'user.history.rows' | transloco }}</th>
              <td mat-cell *matCellDef="let log">{{ log.rowsReturned }}</td>
            </ng-container>

            <ng-container matColumnDef="isSuccess">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'user.history.status' | transloco }}</th>
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

            <ng-container matColumnDef="details">
              <th mat-header-cell *matHeaderCellDef>
                <span class="cdk-visually-hidden">{{ 'admin.logs.details' | transloco }}</span>
              </th>
              <td mat-cell *matCellDef="let log">
                <button mat-icon-button type="button" (click)="openDetails(log)"
                        [matTooltip]="'admin.logs.viewDetails' | transloco"
                        [attr.aria-label]="'admin.logs.viewDetails' | transloco">
                  <mat-icon>visibility</mat-icon>
                </button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
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
    .success { color: var(--status-success); cursor: default; }
    .error { color: var(--status-error); cursor: help; }
    table { width: 100%; }
    .params-col { display: flex; flex-direction: column; align-items: flex-start; gap: 4px; padding: 4px 0; }
    .old-rows-trigger {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 1px 8px;
      margin: 0;
      background: transparent;
      border: 1px dashed var(--border-color);
      border-radius: 10px;
      cursor: pointer;
      font: inherit;
      font-size: 12px;
      color: var(--old-values-color);
    }
    .old-rows-trigger:hover {
      background: var(--bg-secondary);
      border-color: var(--accent-primary);
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
export class ExecutionHistoryComponent implements OnInit {
  displayedColumns = ['queryName', 'parameters', 'executedAt', 'executionDurationMs', 'rowsReturned', 'isSuccess', 'details'];
  logs: ExecutionLog[] = [];
  loading = true;
  errorMessage = '';
  totalCount = 0;
  pageNumber = 1;
  pageSize = 25;
  /** Header state. Mirrored into the component because the table is inside an
   *  *ngIf and is destroyed on every load — MatSort's own state does not survive. */
  sortActive = DEFAULT_SORT_BY;
  sortDirection: SortDirection = 'desc';
  sortBy = DEFAULT_SORT_BY;
  sortDescending = true;

  constructor(
    private queryService: QueryService,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadHistory();
  }

  loadHistory(): void {
    this.loading = true;
    this.errorMessage = '';
    this.queryService.getMyHistory({
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
        this.errorMessage = err.error?.message || this.transloco.translate('user.history.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  /**
   * Headers cycle asc -> desc -> unsorted. Clearing falls back to the default
   * newest-first order, which is what the API applies when sortBy is omitted.
   */
  onSortChange(sort: Sort): void {
    this.sortActive = sort.active;
    this.sortDirection = sort.direction;
    this.sortBy = sort.direction ? sort.active : DEFAULT_SORT_BY;
    this.sortDescending = sort.direction ? sort.direction === 'desc' : true;
    this.pageNumber = 1;
    this.loadHistory();
  }

  onPage(event: PageEvent): void {
    this.pageNumber = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.loadHistory();
  }

  openDetails(log: ExecutionLog): void {
    this.dialog.open(ExecutionLogDetailDialogComponent, {
      data: {
        log,
        showUser: false,
        fetchOldRows: log.hasOldValues
          ? (pageNumber: number, pageSize: number) =>
              this.queryService.getMyHistoryOldValues(log.id, pageNumber, pageSize)
          : undefined
      },
      width: '760px',
      maxWidth: '95vw',
      autoFocus: 'dialog',
      ariaModal: true
    });
  }

  openOldRowsDialog(log: ExecutionLog): void {
    if (!log.hasOldValues) return;
    this.dialog.open(OldRowsDialogComponent, {
      data: {
        queryName: log.queryName,
        fetch: (pageNumber: number, pageSize: number) =>
          this.queryService.getMyHistoryOldValues(log.id, pageNumber, pageSize)
      },
      width: '720px',
      maxWidth: '95vw',
      // autoFocus was false, so keyboard focus never entered the dialog (WCAG 2.4.3).
      autoFocus: 'dialog',
      ariaModal: true
    });
  }
}
