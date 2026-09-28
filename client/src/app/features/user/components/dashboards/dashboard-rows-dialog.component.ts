import { Component, OnDestroy, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { TranslocoService } from '@jsverse/transloco';
import { DashboardService } from '@core/services/dashboard.service';
import { QueryService } from '@core/services/query.service';
import { extractApiError } from '@core/services/toast.service';
import { DashboardTileRows } from '@core/models/dashboard.model';

export interface DashboardRowsDialogData {
  dashboardId: string;
  tileId: string;
  title: string;
  filters: Record<string, string>;
}

/**
 * A tile's underlying rows, for the drill-to-rows action.
 *
 * <p>Runs the tile's query in full on the server and pages the result through the same grid the
 * report viewer uses, so sorting and column filtering behave exactly as they do there. The cached
 * rows are released when the dialog closes.</p>
 */
@Component({
  selector: 'app-dashboard-rows-dialog',
  standalone: false,
  template: `
    <h2 mat-dialog-title dir="auto">{{ data.title }}</h2>
    <mat-dialog-content>
      <div class="loading" *ngIf="loading">
        <mat-progress-spinner mode="indeterminate" diameter="40"></mat-progress-spinner>
      </div>
      <div class="error-block" *ngIf="error">
        <span class="error-text">{{ error }}</span>
      </div>
      <p class="muted" *ngIf="rows">
        {{ 'user.dashboards.rowCount' | transloco: { count: rows.totalRows } }}
      </p>
      <app-report-section-grid *ngIf="rows" [jobId]="rows.jobId" [columns]="rows.columns">
      </app-report-section-grid>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.close' | transloco }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .muted { color: var(--text-secondary); margin: 0 0 8px; }
  `]
})
export class DashboardRowsDialogComponent implements OnInit, OnDestroy {
  data = inject<DashboardRowsDialogData>(MAT_DIALOG_DATA);
  private dashboards = inject(DashboardService);
  private queries = inject(QueryService);
  private transloco = inject(TranslocoService);
  private cdr = inject(ChangeDetectorRef);

  rows: DashboardTileRows | null = null;
  loading = true;
  error = '';

  ngOnInit(): void {
    this.dashboards.runTileRows(this.data.dashboardId, this.data.tileId, this.data.filters).subscribe({
      next: rows => {
        this.rows = rows;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: err => {
        this.error = extractApiError(err, this.transloco.translate('user.dashboards.rowsFailed'));
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  ngOnDestroy(): void {
    if (this.rows) {
      this.queries.releaseJob(this.rows.jobId).subscribe({ error: () => { /* best effort */ } });
    }
  }
}
