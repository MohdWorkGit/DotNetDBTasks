import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { MatSnackBar } from '@angular/material/snack-bar';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { AuthService } from '@core/services/auth.service';
import { QueryGroup } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-query-groups-list',
  template: `
    <div class="container">
      <div class="header">
        <h2>Query Groups</h2>
        <button mat-raised-button color="primary" routerLink="/admin/query-groups/create"
                *ngIf="authService.isAdmin()">
          <mat-icon>add</mat-icon> Create Group
        </button>
      </div>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <div *ngIf="!loading" class="table-toolbar">
            <mat-form-field appearance="outline" class="filter-field">
              <mat-label>Filter groups</mat-label>
              <input matInput (keyup)="applyFilter($event)" placeholder="Search by name or description">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>
          </div>

          <table mat-table [dataSource]="dataSource" matSort *ngIf="!loading">
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th>
              <td mat-cell *matCellDef="let g">{{ g.name }}</td>
            </ng-container>

            <ng-container matColumnDef="description">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Description</th>
              <td mat-cell *matCellDef="let g">{{ g.description | slice:0:80 }}{{ g.description?.length > 80 ? '…' : '' }}</td>
            </ng-container>

            <ng-container matColumnDef="queryCount">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Queries</th>
              <td mat-cell *matCellDef="let g">{{ g.queryCount }}</td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>Actions</th>
              <td mat-cell *matCellDef="let g">
                <button mat-icon-button matTooltip="Edit"
                        [routerLink]="['/admin/query-groups/edit', g.id]"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>edit</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Manage Access"
                        [routerLink]="['/admin/query-groups', g.id, 'access']">
                  <mat-icon>security</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Delete" color="warn"
                        (click)="deleteGroup(g.id, g.name)"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>delete</mat-icon>
                </button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>

            <tr class="mat-row no-data-row" *matNoDataRow>
              <td class="mat-cell no-data-cell" [attr.colspan]="displayedColumns.length">
                No groups match the current filter.
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
    .table-toolbar { margin-bottom: 8px; }
    .filter-field { width: 100%; max-width: 480px; }
    .no-data-row { height: 56px; }
    .no-data-cell { text-align: center; color: var(--text-secondary); padding: 16px; }
    table { width: 100%; }
  `]
})
export class QueryGroupsListComponent implements OnInit {
  displayedColumns = ['name', 'description', 'queryCount', 'actions'];
  dataSource = new MatTableDataSource<QueryGroup>();
  loading = true;

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private queryService: QueryService,
    public authService: AuthService,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.queryService.getAllQueryGroups().pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (groups) => {
        this.dataSource.data = groups;
        this.dataSource.sortingDataAccessor = (item: QueryGroup, property: string) => {
          switch (property) {
            case 'name': return (item.name || '').toLowerCase();
            case 'description': return (item.description || '').toLowerCase();
            case 'queryCount': return item.queryCount || 0;
            default: return (item as any)[property];
          }
        };
        this.dataSource.filterPredicate = (data: QueryGroup, filter: string) => {
          if (!filter) return true;
          const haystack = `${data.name || ''} ${data.description || ''}`.toLowerCase();
          return haystack.includes(filter);
        };
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
        this.snackBar.open('Failed to load query groups', 'Close', { duration: 5000 });
        this.cdr.detectChanges();
      }
    });
  }

  applyFilter(event: Event): void {
    this.dataSource.filter = (event.target as HTMLInputElement).value.trim().toLowerCase();
    if (this.dataSource.paginator) this.dataSource.paginator.firstPage();
  }

  deleteGroup(id: string, name: string): void {
    if (!confirm(`Delete group "${name}"? Queries inside the group will remain but become ungrouped.`)) return;

    this.queryService.deleteQueryGroup(id).subscribe({
      next: () => {
        this.snackBar.open('Group deleted', 'Close', { duration: 3000 });
        this.load();
      },
      error: () => {
        this.snackBar.open('Failed to delete group', 'Close', { duration: 5000 });
      }
    });
  }
}
