import { Component, OnInit, ViewChild, ChangeDetectorRef, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatTableDataSource } from '@angular/material/table';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { TranslocoService } from '@jsverse/transloco';
import { ReportService } from '@core/services/report.service';
import { ToastService, extractApiError } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { AuthService } from '@core/services/auth.service';
import { ReportSummary } from '@core/models/report.model';
import { PERM } from '@core/models/permissions';

@Component({
  selector: 'app-reports-list',
  standalone: false,
  template: `
    <div class="container">
      <div class="page-header">
        <h1>{{ 'admin.reports.title' | transloco }}</h1>
        <span class="spacer"></span>
        <button mat-flat-button color="primary" *ngIf="canManage" (click)="create()">
          <mat-icon>add</mat-icon> {{ 'admin.reports.create' | transloco }}
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
              <mat-label>{{ 'admin.reports.filter' | transloco }}</mat-label>
              <input matInput (input)="applyFilter($event)" [value]="filterValue" dir="auto" />
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>
          </div>

          <div class="table-wrapper">
            <table mat-table [dataSource]="dataSource" matSort>
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>
                  {{ 'admin.reports.name' | transloco }}
                </th>
                <td mat-cell *matCellDef="let r">
                  <div class="name-cell" dir="auto">
                    <strong>{{ r.name }}</strong>
                    <small *ngIf="r.description">{{ r.description }}</small>
                  </div>
                </td>
              </ng-container>

              <ng-container matColumnDef="datasets">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>
                  {{ 'admin.reports.datasets' | transloco }}
                </th>
                <td mat-cell *matCellDef="let r" class="numeric-cell">{{ r.datasetCount }}</td>
              </ng-container>

              <ng-container matColumnDef="parameters">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>
                  {{ 'admin.reports.parameters' | transloco }}
                </th>
                <td mat-cell *matCellDef="let r" class="numeric-cell">{{ r.parameterCount }}</td>
              </ng-container>

              <ng-container matColumnDef="template">
                <th mat-header-cell *matHeaderCellDef>
                  {{ 'admin.reports.template' | transloco }}
                </th>
                <td mat-cell *matCellDef="let r">
                  <span class="type-chip" [class.type-write]="!r.hasTemplate">
                    {{ (r.hasTemplate ? 'admin.reports.templateSet' : 'admin.reports.templateStarter')
                       | transloco }}
                  </span>
                </td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>
                  {{ 'admin.reports.status' | transloco }}
                </th>
                <td mat-cell *matCellDef="let r">
                  <span [style.color]="r.isEnabled ? 'var(--status-active)' : 'var(--status-inactive)'">
                    {{ (r.isEnabled ? 'common.active' : 'common.inactive') | transloco }}
                  </span>
                </td>
              </ng-container>

              <ng-container matColumnDef="actions">
                <th mat-header-cell *matHeaderCellDef></th>
                <td mat-cell *matCellDef="let r">
                  <button mat-icon-button [matMenuTriggerFor]="menu"
                          [attr.aria-label]="'common.actions' | transloco">
                    <mat-icon>more_vert</mat-icon>
                  </button>
                  <mat-menu #menu="matMenu">
                    <button mat-menu-item *ngIf="canManage" (click)="edit(r)">
                      <mat-icon>edit</mat-icon> {{ 'common.edit' | transloco }}
                    </button>
                    <button mat-menu-item *ngIf="canManageAccess" (click)="access(r)">
                      <mat-icon>group</mat-icon> {{ 'admin.reports.access' | transloco }}
                    </button>
                    <button mat-menu-item *ngIf="canManage" (click)="remove(r)">
                      <mat-icon>delete</mat-icon> {{ 'common.delete' | transloco }}
                    </button>
                  </mat-menu>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-row *matRowDef="let row; columns: columns;"></tr>
              <tr class="mat-row no-data-row" *matNoDataRow>
                <td class="no-data-cell" [attr.colspan]="columns.length">
                  {{ 'admin.reports.none' | transloco }}
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
export class ReportsListComponent implements OnInit {
  private reports = inject(ReportService);
  private router = inject(Router);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);
  private transloco = inject(TranslocoService);
  private authService = inject(AuthService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  dataSource = new MatTableDataSource<ReportSummary>([]);
  columns = ['name', 'datasets', 'parameters', 'template', 'status', 'actions'];
  loading = true;
  error = '';
  filterValue = '';

  canManage = false;
  canManageAccess = false;

  ngOnInit(): void {
    this.canManage = this.authService.has(PERM.reportsManage);
    this.canManageAccess = this.authService.has(PERM.accessManageReport);

    this.dataSource.filterPredicate = (report, filter) =>
      (report.name + ' ' + (report.description ?? '')).toLowerCase().includes(filter);

    this.load();
  }

  private load(): void {
    this.loading = true;
    this.reports.getAll().subscribe({
      next: list => {
        this.dataSource.data = list;
        this.dataSource.paginator = this.paginator;
        this.dataSource.sort = this.sort;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: err => {
        this.error = extractApiError(err, this.transloco.translate('admin.reports.loadFailed'));
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
    this.router.navigate(['/admin/reports/create']);
  }

  edit(report: ReportSummary): void {
    this.router.navigate(['/admin/reports/edit', report.id]);
  }

  access(report: ReportSummary): void {
    this.router.navigate(['/admin/reports', report.id, 'access']);
  }

  remove(report: ReportSummary): void {
    this.confirmService.askThen({
      titleKey: 'admin.reports.deleteTitle',
      messageKey: 'admin.reports.deleteMessage',
      params: { name: report.name },
      confirmText: this.transloco.translate('common.delete'),
      destructive: true
    }, () => {
      this.reports.delete(report.id).subscribe({
        next: () => {
          this.toast.success('admin.reports.deleted');
          this.load();
        },
        error: err => this.toast.error(err, 'admin.reports.deleteFailed')
      });
    });
  }
}
