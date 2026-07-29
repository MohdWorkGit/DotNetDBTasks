import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { ActivatedRoute, Router } from '@angular/router';
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
        <div class="header-actions">
          <input #defaultTplInput type="file" accept=".docx" hidden
                 (change)="onDefaultTemplateSelected($event)">
          <button mat-stroked-button [matMenuTriggerFor]="defaultTplMenu"
                  *ngIf="authService.isAdmin()"
                  matTooltip="The Word document layout used by exports when a query has no template of its own">
            <mat-icon>article</mat-icon>
            Default Word Template
            <mat-icon iconPositionEnd>arrow_drop_down</mat-icon>
          </button>
          <mat-menu #defaultTplMenu="matMenu">
            <div class="tpl-menu-status" (click)="$event.stopPropagation()">
              <ng-container *ngIf="templateInfoFailed">Template status unavailable</ng-container>
              <ng-container *ngIf="!templateInfoFailed && !defaultTemplateInfo">Checking template…</ng-container>
              <ng-container *ngIf="defaultTemplateInfo">
                {{ defaultTemplateInfo.isBuiltIn
                    ? 'Using the built-in layout'
                    : 'Custom: ' + (defaultTemplateInfo.fileName || 'unnamed file') }}
              </ng-container>
            </div>
            <button mat-menu-item (click)="downloadDefaultTemplate()"
                    [disabled]="!defaultTemplateInfo">
              <mat-icon>download</mat-icon>
              Download {{ defaultTemplateInfo?.isBuiltIn ? 'starter template (edit and re-upload)' : 'current template' }}
            </button>
            <button mat-menu-item (click)="defaultTplInput.click()">
              <mat-icon>upload_file</mat-icon> Upload new default
            </button>
            <button mat-menu-item (click)="resetDefaultTemplate()"
                    [disabled]="!defaultTemplateInfo || defaultTemplateInfo.isBuiltIn">
              <mat-icon>restart_alt</mat-icon> Reset to built-in layout
            </button>
          </mat-menu>
          <button mat-raised-button color="primary" routerLink="/admin/queries/create"
                  *ngIf="authService.isAdmin()">
            <mat-icon>add</mat-icon> Create Query
          </button>
        </div>
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
              <mat-select [(value)]="statusFilter" (selectionChange)="refreshFilter()">
                <mat-option value="all">All</mat-option>
                <mat-option value="active">Active</mat-option>
                <mat-option value="disabled">Disabled</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline" class="select-filter">
              <mat-label>Group</mat-label>
              <mat-select [(value)]="groupFilter" (selectionChange)="refreshFilter()">
                <mat-option value="all">All</mat-option>
                <mat-option [value]="UNGROUPED">Ungrouped</mat-option>
                <mat-option *ngFor="let g of groupOptions" [value]="g">{{ g }}</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline" class="select-filter">
              <mat-label>DB User</mat-label>
              <mat-select [(value)]="dbUserFilter" (selectionChange)="refreshFilter()">
                <mat-option value="all">All</mat-option>
                <mat-option *ngFor="let u of dbUserOptions" [value]="u">{{ u }}</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <div class="table-wrapper">
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
                <button mat-icon-button matTooltip="Edit" aria-label="Edit"
                        [routerLink]="['/admin/queries/edit', q.id]"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>edit</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Copy" aria-label="Copy"
                        routerLink="/admin/queries/create"
                        [queryParams]="{ copyFrom: q.id }"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>content_copy</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Manage Access" aria-label="Manage Access"
                        [routerLink]="['/admin/queries', q.id, 'roles']">
                  <mat-icon>security</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Delete" aria-label="Delete" color="warn"
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
          </div>

          <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons>
          </mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .header-actions { display: flex; gap: 12px; align-items: center; }
    .tpl-menu-status {
      padding: 8px 16px;
      font-size: 12px;
      color: var(--text-secondary);
      border-bottom: 1px solid var(--border-color);
      cursor: default;
    }
    .filter-field { flex: 1; min-width: 240px; }
    .status-filter { width: 160px; }
    .select-filter { width: 200px; }
    .status-active { color: var(--status-active); font-weight: 500; }
    .status-inactive { color: var(--status-inactive); font-weight: 500; }
    table { width: 100%; }
  `]
})
export class QueryListComponent implements OnInit {
  displayedColumns = ['name', 'description', 'isEnabled', 'queryGroupName', 'databaseUserName', 'parameters', 'actions'];
  dataSource = new MatTableDataSource<DynamicQuery>();
  loading = true;
  statusFilter: 'all' | 'active' | 'disabled' = 'all';
  /// Sentinel that cannot collide with a real group name.
  readonly UNGROUPED = '__ungrouped__';
  groupFilter = 'all';
  dbUserFilter = 'all';
  groupOptions: string[] = [];
  dbUserOptions: string[] = [];
  defaultTemplateInfo: { fileName: string | null; isBuiltIn: boolean } | null = null;
  /** True when the template-info fetch failed, so the menu can say so instead of guessing. */
  templateInfoFailed = false;
  private textFilter = '';

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private queryService: QueryService,
    public authService: AuthService,
    private toast: ToastService,
    private confirmService: ConfirmService,
    private router: Router,
    private route: ActivatedRoute,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    // Deep link from the groups list: /admin/queries?group=<name> (or "ungrouped").
    const group = this.route.snapshot.queryParamMap.get('group');
    if (group) this.groupFilter = group.toLowerCase() === 'ungrouped' ? this.UNGROUPED : group;
    this.loadQueries();
    if (this.authService.isAdmin()) this.loadDefaultTemplateInfo();
  }

  // ---- Default Word template ----

  private loadDefaultTemplateInfo(): void {
    this.templateInfoFailed = false;
    this.queryService.getDefaultWordTemplateInfo().subscribe({
      next: (info) => {
        this.defaultTemplateInfo = info;
        this.cdr.detectChanges();
      },
      // Swallowing this used to leave defaultTemplateInfo null while the menu
      // still rendered 'Custom: ' + fileName — i.e. a literal "Custom: undefined".
      error: () => {
        this.defaultTemplateInfo = null;
        this.templateInfoFailed = true;
        this.cdr.detectChanges();
      }
    });
  }

  downloadDefaultTemplate(): void {
    this.queryService.downloadDefaultWordTemplate().subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = this.defaultTemplateInfo?.fileName || 'default-word-template.docx';
        a.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => this.toast.error(err, 'Failed to download template')
    });
  }

  onDefaultTemplateSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.queryService.uploadDefaultWordTemplate(file).subscribe({
      next: () => {
        this.toast.success('Default Word template updated');
        this.loadDefaultTemplateInfo();
      },
      error: (err) => {
        this.toast.error(err, 'Failed to upload template');
        this.cdr.detectChanges();
      }
    });
  }

  resetDefaultTemplate(): void {
    this.confirmService.askThen({
      title: 'Reset default template?',
      message: 'This deletes the uploaded system-wide Word template and restores the built-in '
        + 'layout. Every query without its own template will use the built-in layout from now on. '
        + 'This cannot be undone.',
      confirmText: 'Reset template',
      destructive: true
    }, () => {
      this.queryService.deleteDefaultWordTemplate().subscribe({
        next: () => {
          this.toast.success('Default template reset to the built-in layout');
          this.loadDefaultTemplateInfo();
        },
        error: (err) => {
          this.toast.error(err, 'Failed to reset template');
          this.cdr.detectChanges();
        }
      });
    });
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
        this.groupOptions = [...new Set(queries.map(q => q.queryGroupName).filter((g): g is string => !!g))]
          .sort((a, b) => a.localeCompare(b));
        this.dbUserOptions = [...new Set(queries.map(q => q.databaseUserName || 'Default'))]
          .sort((a, b) => a.localeCompare(b));
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
          const f = JSON.parse(filter) as {
            text: string; status: 'all' | 'active' | 'disabled'; group: string; dbUser: string;
          };
          if (f.status === 'active' && !data.isEnabled) return false;
          if (f.status === 'disabled' && data.isEnabled) return false;
          if (f.group !== 'all') {
            if (f.group === this.UNGROUPED) {
              if (data.queryGroupName) return false;
            } else if (data.queryGroupName !== f.group) {
              return false;
            }
          }
          if (f.dbUser !== 'all' && (data.databaseUserName || 'Default') !== f.dbUser) return false;
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
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'Failed to load queries');
        this.cdr.detectChanges();
      }
    });
  }

  applyFilter(event: Event): void {
    this.textFilter = (event.target as HTMLInputElement).value.trim().toLowerCase();
    this.refreshFilter();
  }

  refreshFilter(): void {
    this.dataSource.filter = JSON.stringify({
      text: this.textFilter,
      status: this.statusFilter,
      group: this.groupFilter,
      dbUser: this.dbUserFilter
    });
    if (this.dataSource.paginator) this.dataSource.paginator.firstPage();
  }

  deleteQuery(id: string, name: string): void {
    this.confirmService.askThen({
      title: 'Delete query?',
      message: `"${name}" will be permanently deleted, along with its parameters and access `
        + 'assignments. This cannot be undone.',
      confirmText: 'Delete',
      destructive: true
    }, () => {
      this.queryService.deleteQuery(id).subscribe({
        next: () => {
          this.toast.success('Query deleted');
          this.loadQueries();
        },
        error: (err) => {
          this.toast.error(err, 'Failed to delete query');
        }
      });
    });
  }
}
