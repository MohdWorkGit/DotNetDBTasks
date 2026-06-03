import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { AuthService } from '@core/services/auth.service';
import { DynamicQuery } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-query-list',
  template: `
    <div class="container">
      <div class="header">
        <h2>Dynamic Queries</h2>
        <button mat-raised-button color="primary" routerLink="/admin/queries/create"
                *ngIf="authService.isAdmin()">
          <mat-icon>add</mat-icon> Create Query
        </button>
      </div>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <div *ngIf="!loading" class="table-toolbar">
            <mat-form-field appearance="outline" class="filter-field">
              <mat-label>Filter queries</mat-label>
              <input matInput (keyup)="applyFilter($event)" placeholder="Search by name, description, group, DB user...">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>

            <mat-form-field appearance="outline" class="status-filter">
              <mat-label>Status</mat-label>
              <mat-select [(value)]="statusFilter" (selectionChange)="applyStatusFilter()">
                <mat-option value="all">All</mat-option>
                <mat-option value="active">Active</mat-option>
                <mat-option value="disabled">Disabled</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <table mat-table [dataSource]="dataSource" matSort *ngIf="!loading">
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th>
              <td mat-cell *matCellDef="let q">{{ q.name }}</td>
            </ng-container>

            <ng-container matColumnDef="description">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Description</th>
              <td mat-cell *matCellDef="let q">{{ q.description | slice:0:80 }}{{ q.description?.length > 80 ? '…' : '' }}</td>
            </ng-container>

            <ng-container matColumnDef="isEnabled">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
              <td mat-cell *matCellDef="let q">
                <span [class]="q.isEnabled ? 'status-active' : 'status-inactive'">
                  {{ q.isEnabled ? 'Active' : 'Disabled' }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="databaseUserName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>DB User</th>
              <td mat-cell *matCellDef="let q">{{ q.databaseUserName || 'Default' }}</td>
            </ng-container>

            <ng-container matColumnDef="queryGroupName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Group</th>
              <td mat-cell *matCellDef="let q">
                <span *ngIf="q.queryGroupName; else ungrouped">{{ q.queryGroupName }}</span>
                <ng-template #ungrouped><span class="ungrouped">—</span></ng-template>
              </td>
            </ng-container>

            <ng-container matColumnDef="parameters">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Parameters</th>
              <td mat-cell *matCellDef="let q">{{ q.parameters?.length || 0 }}</td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>Actions</th>
              <td mat-cell *matCellDef="let q">
                <button mat-icon-button matTooltip="Edit"
                        [routerLink]="['/admin/queries/edit', q.id]"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>edit</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Copy"
                        routerLink="/admin/queries/create"
                        [queryParams]="{ copyFrom: q.id }"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>content_copy</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Manage Access"
                        [routerLink]="['/admin/queries', q.id, 'roles']">
                  <mat-icon>security</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Delete" color="warn"
                        (click)="deleteQuery(q.id, q.name)"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>delete</mat-icon>
                </button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>

            <tr class="mat-row no-data-row" *matNoDataRow>
              <td class="mat-cell no-data-cell" [attr.colspan]="displayedColumns.length">
                No queries match the current filters.
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
    .header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }
    .loading { display: flex; justify-content: center; padding: 40px; }
    .table-toolbar { display: flex; gap: 12px; align-items: flex-start; margin-bottom: 8px; }
    .filter-field { flex: 1; min-width: 240px; }
    .status-filter { width: 160px; }
    .status-active { color: var(--status-active); font-weight: 500; }
    .status-inactive { color: var(--status-inactive); font-weight: 500; }
    .no-data-row { height: 56px; }
    .no-data-cell { text-align: center; color: var(--text-secondary); padding: 16px; }
    table { width: 100%; }
  `]
})
export class QueryListComponent implements OnInit {
  displayedColumns = ['name', 'description', 'isEnabled', 'queryGroupName', 'databaseUserName', 'parameters', 'actions'];
  dataSource = new MatTableDataSource<DynamicQuery>();
  loading = true;
  statusFilter: 'all' | 'active' | 'disabled' = 'all';
  private textFilter = '';

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private queryService: QueryService,
    public authService: AuthService,
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
        this.dataSource.sortingDataAccessor = (item: DynamicQuery, property: string) => {
          switch (property) {
            case 'isEnabled': return item.isEnabled ? 1 : 0;
            case 'queryGroupName': return (item.queryGroupName || '').toLowerCase();
            case 'databaseUserName': return (item.databaseUserName || 'Default').toLowerCase();
            case 'parameters': return item.parameters?.length || 0;
            case 'description': return (item.description || '').toLowerCase();
            case 'name': return (item.name || '').toLowerCase();
            default: return (item as any)[property];
          }
        };
        this.dataSource.filterPredicate = (data: DynamicQuery, filter: string) => {
          const f = JSON.parse(filter) as { text: string; status: 'all' | 'active' | 'disabled' };
          if (f.status === 'active' && !data.isEnabled) return false;
          if (f.status === 'disabled' && data.isEnabled) return false;
          if (!f.text) return true;
          const haystack = [
            data.name,
            data.description,
            data.queryGroupName,
            data.databaseUserName
          ].filter(Boolean).join(' ').toLowerCase();
          return haystack.includes(f.text);
        };
        this.refreshFilter();
        this.loading = false;
        this.cdr.detectChanges();
        // Bind paginator/sort after the *ngIf table has rendered
        setTimeout(() => {
          this.dataSource.paginator = this.paginator;
          this.dataSource.sort = this.sort;
        });
      },
      error: () => {
        this.loading = false;
        this.snackBar.open('Failed to load queries', 'Close', { duration: 5000 });
        this.cdr.detectChanges();
      }
    });
  }

  applyFilter(event: Event): void {
    this.textFilter = (event.target as HTMLInputElement).value.trim().toLowerCase();
    this.refreshFilter();
  }

  applyStatusFilter(): void {
    this.refreshFilter();
  }

  private refreshFilter(): void {
    this.dataSource.filter = JSON.stringify({ text: this.textFilter, status: this.statusFilter });
    if (this.dataSource.paginator) this.dataSource.paginator.firstPage();
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
