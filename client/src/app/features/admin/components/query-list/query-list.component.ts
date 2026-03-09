import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { DynamicQuery } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-query-list',
  template: `
    <div class="container">
      <div class="header">
        <h2>Dynamic Queries</h2>
        <button mat-raised-button color="primary" routerLink="/admin/queries/create">
          <mat-icon>add</mat-icon> Create Query
        </button>
      </div>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <table mat-table [dataSource]="dataSource" matSort *ngIf="!loading">
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th>
              <td mat-cell *matCellDef="let q">{{ q.name }}</td>
            </ng-container>

            <ng-container matColumnDef="description">
              <th mat-header-cell *matHeaderCellDef>Description</th>
              <td mat-cell *matCellDef="let q">{{ q.description | slice:0:80 }}...</td>
            </ng-container>

            <ng-container matColumnDef="isEnabled">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
              <td mat-cell *matCellDef="let q">
                <span [class]="q.isEnabled ? 'status-active' : 'status-inactive'">
                  {{ q.isEnabled ? 'Active' : 'Disabled' }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="parameters">
              <th mat-header-cell *matHeaderCellDef>Parameters</th>
              <td mat-cell *matCellDef="let q">{{ q.parameters?.length || 0 }}</td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>Actions</th>
              <td mat-cell *matCellDef="let q">
                <button mat-icon-button matTooltip="Edit"
                        [routerLink]="['/admin/queries/edit', q.id]">
                  <mat-icon>edit</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Manage Access"
                        [routerLink]="['/admin/queries', q.id, 'roles']">
                  <mat-icon>security</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Delete" color="warn"
                        (click)="deleteQuery(q.id, q.name)">
                  <mat-icon>delete</mat-icon>
                </button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
          </table>

          <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons>
          </mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }
    .loading { display: flex; justify-content: center; padding: 40px; }
    .status-active { color: var(--status-active); font-weight: 500; }
    .status-inactive { color: var(--status-inactive); font-weight: 500; }
    table { width: 100%; }
  `]
})
export class QueryListComponent implements OnInit {
  displayedColumns = ['name', 'description', 'isEnabled', 'parameters', 'actions'];
  dataSource = new MatTableDataSource<DynamicQuery>();
  loading = true;

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private queryService: QueryService,
    private snackBar: MatSnackBar,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.loadQueries();
  }

  loadQueries(): void {
    this.loading = true;
    this.queryService.getAllQueries().pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (queries) => {
        this.dataSource.data = queries;
        this.dataSource.paginator = this.paginator;
        this.dataSource.sort = this.sort;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.snackBar.open('Failed to load queries', 'Close', { duration: 5000 });
        this.cdr.detectChanges();
      }
    });
  }

  deleteQuery(id: string, name: string): void {
    if (!confirm(`Are you sure you want to delete "${name}"?`)) return;

    this.queryService.deleteQuery(id).subscribe({
      next: () => {
        this.snackBar.open('Query deleted', 'Close', { duration: 3000 });
        this.loadQueries();
      },
      error: () => {
        this.snackBar.open('Failed to delete query', 'Close', { duration: 5000 });
      }
    });
  }
}
