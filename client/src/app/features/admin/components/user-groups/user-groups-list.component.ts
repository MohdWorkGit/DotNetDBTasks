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
import { PERM } from '@core/models/permissions';
import { UserGroup } from '@core/models/dynamic-query.model';
import { TranslocoService } from '@jsverse/transloco';

@Component({
  standalone: false,
  selector: 'app-user-groups-list',
  template: `
    <div class="container">
      <div class="header">
        <h2>{{ 'admin.userGroups.title' | transloco }}</h2>
        <button mat-raised-button color="primary" routerLink="/admin/user-groups/create"
                *ngIf="canManage">
          <mat-icon>group_add</mat-icon> {{ 'admin.userGroups.create' | transloco }}
        </button>
      </div>

      <p class="page-hint">{{ 'admin.userGroups.hint' | transloco }}</p>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <div *ngIf="!loading" class="table-toolbar">
            <mat-form-field appearance="outline" class="filter-field">
              <mat-label>{{ 'admin.userGroups.filter' | transloco }}</mat-label>
              <input matInput (keyup)="applyFilter($event)" [attr.placeholder]="'admin.userGroups.filterPlaceholder' | transloco">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>
          </div>

          <div class="table-wrapper">
          <table mat-table [dataSource]="dataSource" matSort *ngIf="!loading">
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.userGroups.name' | transloco }}</th>
              <td mat-cell *matCellDef="let g" dir="auto">{{ g.name }}</td>
            </ng-container>

            <ng-container matColumnDef="description">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.userGroups.description' | transloco }}</th>
              <td mat-cell *matCellDef="let g">{{ g.description | slice:0:80 }}{{ g.description?.length > 80 ? '…' : '' }}</td>
            </ng-container>

            <ng-container matColumnDef="memberCount">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.userGroups.members' | transloco }}</th>
              <td mat-cell *matCellDef="let g">{{ g.memberCount }}</td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>{{ 'common.actions' | transloco }}</th>
              <td mat-cell *matCellDef="let g">
                <button mat-icon-button [matTooltip]="'common.edit' | transloco" [attr.aria-label]="'common.edit' | transloco"
                        [routerLink]="['/admin/user-groups/edit', g.id]"
                        *ngIf="canManage">
                  <mat-icon>edit</mat-icon>
                </button>
                <button mat-icon-button [matTooltip]="'common.delete' | transloco" [attr.aria-label]="'common.delete' | transloco" color="warn"
                        (click)="deleteGroup(g.id, g.name)"
                        *ngIf="canManage">
                  <mat-icon>delete</mat-icon>
                </button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>

            <tr class="mat-row no-data-row" *matNoDataRow>
              <td class="mat-cell no-data-cell" [attr.colspan]="displayedColumns.length">
                {{ 'admin.userGroups.noMatch' | transloco }}
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
    .page-hint { color: var(--text-secondary); margin: 0 0 16px; }
    table { width: 100%; }
  `]
})
export class UserGroupsListComponent implements OnInit {
  readonly PERM = PERM;
  displayedColumns = ['name', 'description', 'memberCount', 'actions'];
  dataSource = new MatTableDataSource<UserGroup>();
  loading = true;

  /**
   * Whether this account may change groups: always for an Admin, and for an Access Manager
   * only while the accessManagerCanManageUserGroups setting is on. The API is the real gate;
   * this keeps the page from offering buttons that would 403.
   */
  canManage = false;

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
    // A permission now, not a runtime setting — one synchronous question.
    this.canManage = this.authService.has(PERM.userGroupsManage);

    this.load();
  }

  load(): void {
    this.loading = true;
    this.queryService.getAllUserGroups().pipe(
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
        this.dataSource.sortingDataAccessor = (item: UserGroup, property: string) => {
          switch (property) {
            case 'name': return (item.name || '').toLowerCase();
            case 'description': return (item.description || '').toLowerCase();
            case 'memberCount': return item.memberCount || 0;
            default: return (item as any)[property];
          }
        };
        this.dataSource.filterPredicate = (data: UserGroup, filter: string) => {
          if (!filter) return true;
          // Members are searchable too: "which group is Sara in?" is the question this
          // page gets asked, and scanning every group by hand is the alternative.
          const members = (data.members || [])
            .map(m => `${m.username} ${m.firstName} ${m.lastName}`)
            .join(' ');
          return `${data.name || ''} ${data.description || ''} ${members}`.toLowerCase().includes(filter);
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
        this.toast.error(err, 'admin.userGroups.loadFailed');
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
      titleKey: 'admin.userGroups.deleteTitle',
      messageKey: 'admin.userGroups.deleteMessage',
      params: { name },
      confirmText: this.transloco.translate('common.delete'),
      destructive: true
    }, () => {
      this.queryService.deleteUserGroup(id).subscribe({
        next: () => {
          this.toast.success('admin.userGroups.deleted');
          this.load();
        },
        error: (err) => {
          this.toast.error(err, 'admin.userGroups.deleteFailed');
        }
      });
    });
  }
}
