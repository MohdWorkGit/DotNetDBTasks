import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoModule } from '@jsverse/transloco';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { Observable } from 'rxjs';
import { OldValuesPage } from '@core/models/dynamic-query.model';

export interface OldRowsDialogData {
  queryName: string;
  /** Endpoint-agnostic page fetcher (admin logs vs. user history). */
  fetch: (pageNumber: number, pageSize: number) => Observable<OldValuesPage>;
}

/**
 * Shows the pre-change row snapshots of an execution log, fetched from the server one
 * page at a time so huge logs (thousands of affected rows) never load all at once.
 */
@Component({
  standalone: true,
  selector: 'app-old-rows-dialog',
  imports: [
    CommonModule,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatTableModule, TranslocoModule],
  template: `
    <h2 mat-dialog-title>Affected rows — {{ data.queryName }}</h2>
    <mat-dialog-content>
      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="36"></mat-spinner>
      </div>

      <div *ngIf="!loading && errorMessage" class="error-block">
        <p class="error-text">{{ errorMessage }}</p>
        <button mat-raised-button color="primary" (click)="loadPage()">
          <mat-icon>refresh</mat-icon> Retry
        </button>
      </div>

      <ng-container *ngIf="!loading && !errorMessage">
        <p class="dialog-subtitle">
          {{ totalRows }} row{{ totalRows === 1 ? '' : 's' }} as they existed before the change.
        </p>
        <div class="rows-table-wrapper">
          <table mat-table [dataSource]="rows" class="rows-table">
            <ng-container *ngFor="let col of columns" [matColumnDef]="col">
              <th mat-header-cell *matHeaderCellDef>{{ col }}</th>
              <td mat-cell *matCellDef="let row">{{ row[col] }}</td>
            </ng-container>
            <tr mat-header-row *matHeaderRowDef="columns"></tr>
            <tr mat-row *matRowDef="let row; columns: columns;"></tr>
          </table>
        </div>
        <mat-paginator [length]="totalRows"
                       [pageIndex]="pageNumber - 1"
                       [pageSize]="pageSize"
                       [pageSizeOptions]="[50, 100, 200]"
                       showFirstLastButtons
                       (page)="onPage($event)">
        </mat-paginator>
      </ng-container>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-stroked-button (click)="close()">{{ 'common.close' | transloco }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-subtitle { font-size: 13px; margin: 0 0 12px 0; color: var(--text-secondary); }
    .rows-table-wrapper {
      overflow-x: auto;
      max-height: 55vh;
      overflow-y: auto;
      border: 1px solid var(--border-color);
      border-radius: 4px;
    }
    .rows-table { width: 100%; font-size: 13px; }
    .rows-table th {
      font-weight: 600;
      background: var(--bg-secondary);
      position: sticky;
      top: 0;
      z-index: 1;
    }
  `]
})
export class OldRowsDialogComponent implements OnInit {
  readonly dialogRef = inject(MatDialogRef<OldRowsDialogComponent>);
  readonly data = inject<OldRowsDialogData>(MAT_DIALOG_DATA);
  private readonly cdr = inject(ChangeDetectorRef);

  loading = true;
  errorMessage = '';
  rows: Record<string, string>[] = [];
  columns: string[] = [];
  totalRows = 0;
  pageNumber = 1;
  pageSize = 100;

  ngOnInit(): void {
    this.loadPage();
  }

  loadPage(): void {
    this.loading = true;
    this.errorMessage = '';
    this.data.fetch(this.pageNumber, this.pageSize).subscribe({
      next: (page) => {
        this.rows = page.rows;
        this.columns = page.columns;
        this.totalRows = page.totalRows;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || 'Failed to load rows. Please try again.';
        this.cdr.detectChanges();
      }
    });
  }

  onPage(event: PageEvent): void {
    this.pageNumber = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    this.loadPage();
  }

  close(): void {
    this.dialogRef.close();
  }
}
