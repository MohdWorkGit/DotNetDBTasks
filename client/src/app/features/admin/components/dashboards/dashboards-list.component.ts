import { Component, OnInit, ViewChild, ChangeDetectorRef, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatTableDataSource } from '@angular/material/table';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { TranslocoService } from '@jsverse/transloco';
import { DashboardService } from '@core/services/dashboard.service';
import { ToastService, extractApiError } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { AuthService } from '@core/services/auth.service';
import { DashboardSummary } from '@core/models/dashboard.model';
import { PERM } from '@core/models/permissions';

@Component({
  selector: 'app-dashboards-admin-list',
  standalone: false,
  template: `
    <div class="container">
      <div class="page-header">
        <h1>{{ 'admin.dashboards.title' | transloco }}</h1>
        <span class="spacer"></span>
        <button mat-flat-button color="primary" *ngIf="canManage" (click)="create()">
          <mat-icon>add</mat-icon> {{ 'admin.dashboards.create' | transloco }}
        </button>
      </div>

      <div class="loading" *ngIf="loading">
        <mat-progress-spinner mode="indeterminate" diameter="40"></mat-progress-spinner>
      </div>

      <div class="error-block" *ngIf="error">
        <span class="error-text">{{ error }}</span>
      </div>

      <mat-card *ngIf="!loading">
        <mat-card-content>
          <div class="table-toolbar">
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>{{ 'admin.dashboards.filter' | transloco }}</mat-label>
              <input matInput (input)="applyFilter($event)" [value]="filterValue" dir="auto" />
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>
          </div>

          <div class="table-wrapper">
            <table mat-table [dataSource]="dataSource" matSort>
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>
                  {{ 'admin.dashboards.name' | transloco }}
                </th>
                <td mat-cell *matCellDef="let d">
                  <div class="name-cell" dir="auto">
                    <strong>{{ d.name }}</strong>
                    <small *ngIf="d.description">{{ d.description }}</small>
                  </div>
                </td>
              </ng-container>

              <ng-container matColumnDef="tileCount">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>
                  {{ 'admin.dashboards.tiles' | transloco }}
                </th>
                <td mat-cell *matCellDef="let d" class="numeric-cell">{{ d.tileCount }}</td>
              </ng-container>

              <ng-container matColumnDef="filterCount">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>
                  {{ 'admin.dashboards.filters' | transloco }}
                </th>
                <td mat-cell *matCellDef="let d" class="numeric-cell">{{ d.filterCount }}</td>
              </ng-container>

              <ng-container matColumnDef="defaultRefreshSeconds">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>
                  {{ 'admin.dashboards.refresh' | transloco }}
                </th>
                <td mat-cell *matCellDef="let d" class="numeric-cell">
                  {{ 'admin.dashboards.seconds' | transloco: { seconds: d.defaultRefreshSeconds } }}
                </td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef mat-sort-header="isEnabled">
                  {{ 'admin.dashboards.status' | transloco }}
                </th>
                <td mat-cell *matCellDef="let d">
                  <span [style.color]="d.isEnabled ? 'var(--status-active)' : 'var(--status-inactive)'">
                    {{ (d.isEnabled ? 'common.active' : 'common.inactive') | transloco }}
                  </span>
                </td>
              </ng-container>

              <ng-container matColumnDef="actions">
                <th mat-header-cell *matHeaderCellDef></th>
                <td mat-cell *matCellDef="let d">
                  <button mat-icon-button [matMenuTriggerFor]="menu"
                          [attr.aria-label]="'common.actions' | transloco">
                    <mat-icon>more_vert</mat-icon>
                  </button>
                  <mat-menu #menu="matMenu">
                    <button mat-menu-item *ngIf="canView" (click)="open(d)">
                      <mat-icon>open_in_new</mat-icon> {{ 'admin.dashboards.open' | transloco }}
                    </button>
                    <button mat-menu-item *ngIf="canManage" (click)="edit(d)">
                      <mat-icon>edit</mat-icon> {{ 'common.edit' | transloco }}
                    </button>
                    <button mat-menu-item *ngIf="canManageAccess" (click)="access(d)">
                      <mat-icon>group</mat-icon> {{ 'admin.dashboards.access' | transloco }}
                    </button>
                    <button mat-menu-item *ngIf="canManage" (click)="remove(d)">
                      <mat-icon>delete</mat-icon> {{ 'common.delete' | transloco }}
                    </button>
                  </mat-menu>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-row *matRowDef="let row; columns: columns;"></tr>
              <tr class="mat-row no-data-row" *matNoDataRow>
                <td class="no-data-cell" [attr.colspan]="columns.length">
                  {{ 'admin.dashboards.none' | transloco }}
                </td>
              </tr>
            </table>
          </div>

          <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons></mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .name-cell { display: flex; flex-direction: column; }
    .name-cell small { color: var(--text-secondary); }
  `]
})
export class DashboardsAdminListComponent implements OnInit {
  private dashboards = inject(DashboardService);
  private router = inject(Router);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);
  private transloco = inject(TranslocoService);
  private authService = inject(AuthService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  dataSource = new MatTableDataSource<DashboardSummary>([]);
  columns = ['name', 'tileCount', 'filterCount', 'defaultRefreshSeconds', 'status', 'actions'];
  loading = true;
  error = '';
  filterValue = '';

  canManage = false;
  canManageAccess = false;
  canView = false;

  ngOnInit(): void {
    this.canManage = this.authService.has(PERM.dashboardsManage);
    this.canManageAccess = this.authService.has(PERM.accessManageDashboard);
    this.canView = this.authService.has(PERM.dashboardsRun);

    this.dataSource.filterPredicate = (d, filter) =>
      (d.name + ' ' + (d.description ?? '')).toLowerCase().includes(filter);

    this.load();
  }

  private load(): void {
    this.loading = true;
    this.dashboards.getAll().subscribe({
      next: list => {
        this.dataSource.data = list;
        this.dataSource.paginator = this.paginator;
        this.dataSource.sort = this.sort;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: err => {
        this.error = extractApiError(err, this.transloco.translate('admin.dashboards.loadFailed'));
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  applyFilter(event: Event): void {
    this.filterValue = (event.target as HTMLInputElement).value;
    this.dataSource.filter = this.filterValue.trim().toLowerCase();
    this.dataSource.paginator?.firstPage();
  }

  create(): void {
    this.router.navigate(['/admin/dashboards/create']);
  }

  open(d: DashboardSummary): void {
    this.router.navigate(['/user/dashboards', d.id]);
  }

  edit(d: DashboardSummary): void {
    this.router.navigate(['/admin/dashboards/edit', d.id]);
  }

  access(d: DashboardSummary): void {
    this.router.navigate(['/admin/dashboards', d.id, 'access']);
  }

  remove(d: DashboardSummary): void {
    this.confirmService.askThen({
      titleKey: 'admin.dashboards.deleteTitle',
      messageKey: 'admin.dashboards.deleteMessage',
      params: { name: d.name },
      confirmText: this.transloco.translate('common.delete'),
      destructive: true
    }, () => {
      this.dashboards.delete(d.id).subscribe({
        next: () => {
          this.toast.success('admin.dashboards.deleted');
          this.load();
        },
        error: err => this.toast.error(err, 'admin.dashboards.deleteFailed')
      });
    });
  }
}
