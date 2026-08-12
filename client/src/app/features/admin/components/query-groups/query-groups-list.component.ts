import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { AuthService } from '@core/services/auth.service';
import { QueryGroup } from '@core/models/dynamic-query.model';
import { TranslocoService } from '@jsverse/transloco';

@Component({
  standalone: false,
  selector: 'app-query-groups-list',
  template: `
    <div class="container">
      <div class="header">
        <h2>{{ 'admin.groups.title' | transloco }}</h2>
        <button mat-raised-button color="primary" routerLink="/admin/query-groups/create"
                *ngIf="authService.isAdmin()">
          <mat-icon>add</mat-icon> {{ 'admin.groups.create' | transloco }}
        </button>
      </div>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <div *ngIf="!loading" class="table-toolbar">
            <mat-form-field appearance="outline" class="filter-field">
              <mat-label>{{ 'admin.groups.filter' | transloco }}</mat-label>
              <input matInput (keyup)="applyFilter($event)" [attr.placeholder]="'admin.groups.filterPlaceholder' | transloco">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>
          </div>

          <div class="table-wrapper">
          <table mat-table [dataSource]="dataSource" matSort *ngIf="!loading">
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.groups.name' | transloco }}</th>
              <td mat-cell *matCellDef="let g" dir="auto">{{ g.name }}</td>
            </ng-container>

            <ng-container matColumnDef="description">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.groups.description' | transloco }}</th>
              <td mat-cell *matCellDef="let g">{{ g.description | slice:0:80 }}{{ g.description?.length > 80 ? '…' : '' }}</td>
            </ng-container>

            <ng-container matColumnDef="queryCount">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.groups.queries' | transloco }}</th>
              <td mat-cell *matCellDef="let g">
                <a class="count-link" [routerLink]="['/admin/queries']" [queryParams]="{ group: g.name }"
                   [matTooltip]="'admin.groups.viewQueries' | transloco">{{ g.queryCount }}</a>
              </td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>{{ 'common.actions' | transloco }}</th>
              <td mat-cell *matCellDef="let g">
                <button mat-icon-button [matTooltip]="'common.edit' | transloco" [attr.aria-label]="'common.edit' | transloco"
                        [routerLink]="['/admin/query-groups/edit', g.id]"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>edit</mat-icon>
                </button>
                <button mat-icon-button [matTooltip]="'admin.common.manageAccess' | transloco" [attr.aria-label]="'admin.common.manageAccess' | transloco"
                        [routerLink]="['/admin/query-groups', g.id, 'access']">
                  <mat-icon>security</mat-icon>
                </button>
                <button mat-icon-button [matTooltip]="'common.delete' | transloco" [attr.aria-label]="'common.delete' | transloco" color="warn"
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
          </div>

          <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons>
          </mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .filter-field { width: 100%; max-width: 480px; }
    .count-link { color: inherit; text-decoration: underline; cursor: pointer; }
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
    private toast: ToastService,
    private confirmService: ConfirmService,
    private transloco: TranslocoService,
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
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'admin.groups.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  applyFilter(event: Event): void {
    this.dataSource.filter = (event.target as HTMLInputElement).value.trim().toLowerCase();
    if (this.dataSource.paginator) this.dataSource.paginator.firstPage();
  }

  deleteGroup(id: string, name: string): void {
    this.confirmService.askThen({
      titleKey: 'admin.groups.deleteTitle',
      messageKey: 'admin.groups.deleteMessage',
      params: { name },
      confirmText: this.transloco.translate('common.delete'),
      destructive: true
    }, () => {
      this.queryService.deleteQueryGroup(id).subscribe({
        next: () => {
          this.toast.success('admin.groups.deleted');
          this.load();
        },
        error: (err) => {
          this.toast.error(err, 'admin.groups.deleteFailed');
        }
      });
    });
  }
}
