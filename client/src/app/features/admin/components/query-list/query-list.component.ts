import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort, SortDirection } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { MatDialog } from '@angular/material/dialog';
import { ListStateService } from '@core/services/list-state.service';
import { ImportResultDialogComponent } from '@shared/components/import-result-dialog.component';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { ActivatedRoute, Router } from '@angular/router';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { AuthService } from '@core/services/auth.service';
import { DynamicQuery, isWriteQueryType, QUERY_TYPE_LABELS, QueryType } from '@core/models/dynamic-query.model';
import { TranslocoService } from '@jsverse/transloco';

/** What survives navigating away from the list and back. */
interface QueryListState {
  text: string;
  status: 'all' | 'active' | 'disabled';
  type: QueryType | 'all';
  group: string;
  dbUser: string;
  pageIndex: number;
  sortActive: string;
  sortDirection: SortDirection;
}

@Component({
  standalone: false,
  selector: 'app-query-list',
  template: `
    <div class="container">
      <div class="header">
        <h2>{{ 'admin.queries.title' | transloco }}</h2>
        <div class="header-actions">
          <input #defaultTplInput type="file" accept=".docx" hidden
                 (change)="onDefaultTemplateSelected($event)">
          <button mat-stroked-button [matMenuTriggerFor]="defaultTplMenu"
                  *ngIf="authService.isAdmin()"
                  [matTooltip]="'admin.queries.defaultTemplateTip' | transloco">
            <mat-icon>article</mat-icon>
            {{ 'admin.queries.defaultWordTemplate' | transloco }}
            <mat-icon iconPositionEnd>arrow_drop_down</mat-icon>
          </button>
          <mat-menu #defaultTplMenu="matMenu">
            <div class="tpl-menu-status" (click)="$event.stopPropagation()">
              <ng-container *ngIf="templateInfoFailed">{{ 'admin.queries.templateStatusUnavailable' | transloco }}</ng-container>
              <ng-container *ngIf="!templateInfoFailed && !defaultTemplateInfo">{{ 'admin.queries.checkingTemplate' | transloco }}</ng-container>
              <ng-container *ngIf="defaultTemplateInfo">
                {{ defaultTemplateInfo.isBuiltIn
                    ? ('admin.queries.usingBuiltIn' | transloco)
                    : ('admin.queries.customTemplate' | transloco: { fileName: defaultTemplateInfo.fileName
                        || ('admin.queries.unnamedFile' | transloco) }) }}
              </ng-container>
            </div>
            <button mat-menu-item (click)="downloadDefaultTemplate()"
                    [disabled]="!defaultTemplateInfo">
              <mat-icon>download</mat-icon>
              {{ (defaultTemplateInfo?.isBuiltIn
                    ? 'admin.queries.downloadStarter'
                    : 'admin.queries.downloadCurrent') | transloco }}
            </button>
            <button mat-menu-item (click)="defaultTplInput.click()">
              <mat-icon>upload_file</mat-icon> {{ 'admin.queries.uploadDefault' | transloco }}
            </button>
            <button mat-menu-item (click)="resetDefaultTemplate()"
                    [disabled]="!defaultTemplateInfo || defaultTemplateInfo.isBuiltIn">
              <mat-icon>restart_alt</mat-icon> {{ 'admin.queries.resetToBuiltIn' | transloco }}
            </button>
          </mat-menu>
          <input #importInput type="file" accept=".json,application/json" hidden
                 (change)="onImportFileSelected($event)">
          <button mat-stroked-button [matMenuTriggerFor]="backupMenu"
                  *ngIf="authService.isAdmin()"
                  [matTooltip]="'admin.queries.backupTip' | transloco">
            <mat-icon>backup</mat-icon>
            {{ 'admin.queries.backup' | transloco }}
            <mat-icon iconPositionEnd>arrow_drop_down</mat-icon>
          </button>
          <mat-menu #backupMenu="matMenu">
            <button mat-menu-item (click)="exportAllQueries()" [disabled]="importing">
              <mat-icon>file_download</mat-icon> {{ 'admin.queries.exportAll' | transloco }}
            </button>
            <button mat-menu-item (click)="importInput.click()"
                    *ngIf="authService.isAdmin()" [disabled]="importing">
              <mat-icon>file_upload</mat-icon> {{ 'admin.queries.importFromBackup' | transloco }}
            </button>
          </mat-menu>
          <button mat-raised-button color="primary" routerLink="/admin/queries/create"
                  *ngIf="authService.isAdmin()">
            <mat-icon>add</mat-icon> {{ 'admin.queries.create' | transloco }}
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
              <mat-label>{{ 'admin.queries.filter' | transloco }}</mat-label>
              <input matInput [value]="textFilter" (keyup)="applyFilter($event)"
                     [attr.placeholder]="'admin.queries.filterPlaceholder' | transloco">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>

            <mat-form-field appearance="outline" class="status-filter">
              <mat-label>{{ 'admin.queries.status' | transloco }}</mat-label>
              <mat-select [(value)]="statusFilter" (selectionChange)="refreshFilter()">
                <mat-option value="all">{{ 'common.all' | transloco }}</mat-option>
                <mat-option value="active">{{ 'common.active' | transloco }}</mat-option>
                <mat-option value="disabled">{{ 'common.disabled' | transloco }}</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline" class="select-filter">
              <mat-label>{{ 'admin.queries.type' | transloco }}</mat-label>
              <mat-select [(value)]="typeFilter" (selectionChange)="refreshFilter()">
                <mat-option value="all">{{ 'common.all' | transloco }}</mat-option>
                <mat-option [value]="QueryType.Select">SELECT</mat-option>
                <mat-option [value]="QueryType.Insert">INSERT</mat-option>
                <mat-option [value]="QueryType.Update">UPDATE</mat-option>
                <mat-option [value]="QueryType.Delete">DELETE</mat-option>
                <mat-option [value]="QueryType.Other">{{ 'admin.queries.typeOther' | transloco }}</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline" class="select-filter">
              <mat-label>{{ 'admin.queries.group' | transloco }}</mat-label>
              <mat-select [(value)]="groupFilter" (selectionChange)="refreshFilter()">
                <mat-option value="all">{{ 'common.all' | transloco }}</mat-option>
                <mat-option [value]="UNGROUPED">{{ 'admin.queries.ungrouped' | transloco }}</mat-option>
                <mat-option *ngFor="let g of groupOptions" [value]="g">{{ g }}</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline" class="select-filter">
              <mat-label>{{ 'admin.queries.dbUser' | transloco }}</mat-label>
              <mat-select [(value)]="dbUserFilter" (selectionChange)="refreshFilter()">
                <mat-option value="all">{{ 'common.all' | transloco }}</mat-option>
                <mat-option *ngFor="let u of dbUserOptions" [value]="u">{{ u }}</mat-option>
              </mat-select>
            </mat-form-field>

            <!-- Filters are remembered across navigation, so there has to be an obvious way to
                 undo them — otherwise a restored filter just looks like missing queries. -->
            <button mat-stroked-button type="button" class="clear-filters"
                    *ngIf="hasActiveFilters" (click)="clearFilters()">
              <mat-icon>filter_alt_off</mat-icon> Clear filters
            </button>
          </div>

          <div class="table-wrapper">
          <table mat-table [dataSource]="dataSource" matSort *ngIf="!loading">
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.queries.name' | transloco }}</th>
              <td mat-cell *matCellDef="let q" dir="auto">{{ q.name }}</td>
            </ng-container>

            <ng-container matColumnDef="description">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.queries.description' | transloco }}</th>
              <td mat-cell *matCellDef="let q">{{ q.description | slice:0:80 }}{{ q.description?.length > 80 ? '…' : '' }}</td>
            </ng-container>

            <ng-container matColumnDef="queryType">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.queries.type' | transloco }}</th>
              <td mat-cell *matCellDef="let q">
                <span class="type-chip" [class.type-write]="isWriteType(q.queryType)">
                  {{ typeLabel(q.queryType) }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="isEnabled">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.queries.status' | transloco }}</th>
              <td mat-cell *matCellDef="let q">
                <span [class]="q.isEnabled ? 'status-active' : 'status-inactive'">
                  {{ (q.isEnabled ? 'common.active' : 'common.disabled') | transloco }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="databaseUserName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.queries.dbUser' | transloco }}</th>
              <td mat-cell *matCellDef="let q">{{ q.databaseUserName || 'Default' }}</td>
            </ng-container>

            <ng-container matColumnDef="queryGroupName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.queries.group' | transloco }}</th>
              <td mat-cell *matCellDef="let q">
                <span *ngIf="q.queryGroupName; else ungrouped">{{ q.queryGroupName }}</span>
                <ng-template #ungrouped><span class="ungrouped">—</span></ng-template>
              </td>
            </ng-container>

            <ng-container matColumnDef="parameters">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.queries.parameters' | transloco }}</th>
              <td mat-cell *matCellDef="let q">{{ q.parameters?.length || 0 }}</td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>{{ 'common.actions' | transloco }}</th>
              <td mat-cell *matCellDef="let q">
                <button mat-icon-button [matTooltip]="'common.edit' | transloco" [attr.aria-label]="'common.edit' | transloco"
                        [routerLink]="['/admin/queries/edit', q.id]"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>edit</mat-icon>
                </button>
                <button mat-icon-button [matTooltip]="'common.copy' | transloco" [attr.aria-label]="'common.copy' | transloco"
                        routerLink="/admin/queries/create"
                        [queryParams]="{ copyFrom: q.id }"
                        *ngIf="authService.isAdmin()">
                  <mat-icon>content_copy</mat-icon>
                </button>
                <button mat-icon-button *ngIf="canManageQueryAccess"
                        [matTooltip]="'admin.common.manageAccess' | transloco" [attr.aria-label]="'admin.common.manageAccess' | transloco"
                        [routerLink]="['/admin/queries', q.id, 'roles']">
                  <mat-icon>security</mat-icon>
                </button>
                <button mat-icon-button (click)="exportQuery(q)"
                        *ngIf="authService.isAdmin()"
                        [matTooltip]="'Export ' + q.name + ' as a JSON definition'"
                        [attr.aria-label]="'Export ' + q.name">
                  <mat-icon>file_download</mat-icon>
                </button>
                <button mat-icon-button [matTooltip]="'common.delete' | transloco" [attr.aria-label]="'common.delete' | transloco" color="warn"
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
    /* mat-form-field carries its own subscript spacing; nudge the button onto the same line. */
    .clear-filters { align-self: flex-start; margin-top: 8px; }
    /* .type-chip / .type-write are global — shared with the execution-logs table. */
    .status-active { color: var(--status-active); font-weight: 500; }
    .status-inactive { color: var(--status-inactive); font-weight: 500; }
    table { width: 100%; }
  `]
})
export class QueryListComponent implements OnInit {
  /**
   * Per-query access is Admin-only unless an administrator has switched it on for Access
   * Managers. Hidden rather than left to 403: the server enforces it either way, but an
   * always-failing button is worse than no button.
   */
  canManageQueryAccess = false;

  displayedColumns = ['name', 'description', 'queryType', 'isEnabled', 'queryGroupName', 'databaseUserName', 'parameters', 'actions'];
  dataSource = new MatTableDataSource<DynamicQuery>();
  loading = true;
  /** Blocks the backup menu while an import is in flight. */
  importing = false;
  statusFilter: 'all' | 'active' | 'disabled' = 'all';
  /// Sentinel that cannot collide with a real group name.
  readonly UNGROUPED = '__ungrouped__';
  typeFilter: QueryType | 'all' = 'all';
  /// Exposed for the template's mat-option values.
  readonly QueryType = QueryType;
  readonly QUERY_TYPE_LABELS = QUERY_TYPE_LABELS;
  groupFilter = 'all';
  dbUserFilter = 'all';
  groupOptions: string[] = [];
  dbUserOptions: string[] = [];
  defaultTemplateInfo: { fileName: string | null; isBuiltIn: boolean } | null = null;
  /** True when the template-info fetch failed, so the menu can say so instead of guessing. */
  templateInfoFailed = false;
  /** Raw as typed — the predicate lowercases at compare time so restoring it into the
   *  input does not echo the user's typing back at them in lower case. */
  textFilter = '';

  private readonly STATE_KEY = 'admin-queries';
  /** Page/sort restored from a previous visit, applied once the table has rendered. */
  private restored: QueryListState | null = null;

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private queryService: QueryService,
    public authService: AuthService,
    private toast: ToastService,
    private confirmService: ConfirmService,
    private router: Router,
    private route: ActivatedRoute,
    private listState: ListStateService,
    private dialog: MatDialog,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.canManageQueryAccess = this.authService.isAdmin();
    if (!this.canManageQueryAccess && this.authService.isAccessManager()) {
      this.queryService.getSystemSettings().subscribe({
        next: (settings) => {
          this.canManageQueryAccess = settings.accessManagerCanManageQueryAccess;
          this.cdr.detectChanges();
        },
        // Leave it hidden on failure — the safe reading matches the server's default.
        error: () => this.cdr.detectChanges()
      });
    }

    // Deep link from the groups list: /admin/queries?group=<name> (or "ungrouped").
    const group = this.route.snapshot.queryParamMap.get('group');
    if (group) {
      // The deep link is an explicit request for one group, so it wins outright. Restoring a
      // saved Status/Type filter on top would show fewer rows than the count just clicked.
      this.groupFilter = group.toLowerCase() === 'ungrouped' ? this.UNGROUPED : group;
      this.listState.clear(this.STATE_KEY);
    } else {
      this.restoreState();
    }
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

  // ---- Backup: export / import ----

  exportQuery(query: DynamicQuery): void {
    this.queryService.exportQuery(query.id).subscribe({
      next: (blob) => this.saveBlob(blob, `${this.slug(query.name)}.json`),
      error: (err) => this.toast.error(err, 'admin.queries.exportOneFailed')
    });
  }

  exportAllQueries(): void {
    this.queryService.exportAllQueries().subscribe({
      next: (blob) => {
        const stamp = new Date().toISOString().slice(0, 10);
        this.saveBlob(blob, `queries-backup-${stamp}.json`);
        this.toast.success('admin.queries.backupDownloaded');
      },
      error: (err) => this.toast.error(err, 'admin.queries.exportFailed')
    });
  }

  onImportFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Clear first so re-picking the same file still fires a change event.
    input.value = '';
    if (!file) return;

    this.confirmService.askThen({
      titleKey: 'admin.queries.importTitle',
      messageKey: 'admin.queries.importMessage',
      params: { fileName: file.name },
      confirmText: this.transloco.translate('admin.queries.import')
    }, () => this.runImport(file));
  }

  private runImport(file: File): void {
    this.importing = true;
    this.cdr.detectChanges();
    this.queryService.importQueries(file).subscribe({
      next: (result) => {
        this.importing = false;
        this.dialog.open(ImportResultDialogComponent, {
          data: result,
          width: '560px',
          autoFocus: 'dialog',
          ariaModal: true
        });
        // The list is stale the moment anything imported, so reload regardless of warnings.
        this.loadQueries();
      },
      error: (err) => {
        this.importing = false;
        this.toast.error(err, 'admin.queries.importFailed');
        this.cdr.detectChanges();
      }
    });
  }

  private saveBlob(blob: Blob, fileName: string): void {
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  /** Mirrors the server's file-name slug so a single-query export lands with a sane name. */
  private slug(name: string): string {
    const cleaned = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
    return cleaned || 'query';
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
      error: (err) => this.toast.error(err, 'admin.queryForm.templateDownloadFailed')
    });
  }

  onDefaultTemplateSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.queryService.uploadDefaultWordTemplate(file).subscribe({
      next: () => {
        this.toast.success('admin.queries.defaultTemplateUpdated');
        this.loadDefaultTemplateInfo();
      },
      error: (err) => {
        this.toast.error(err, 'admin.queryForm.templateUploadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  resetDefaultTemplate(): void {
    this.confirmService.askThen({
      titleKey: 'admin.queries.resetTemplateTitle',
      messageKey: 'admin.queries.resetTemplateMessage',
      confirmText: this.transloco.translate('admin.queries.resetTemplateConfirm'),
      destructive: true
    }, () => {
      this.queryService.deleteDefaultWordTemplate().subscribe({
        next: () => {
          this.toast.success('admin.queries.defaultTemplateReset');
          this.loadDefaultTemplateInfo();
        },
        error: (err) => {
          this.toast.error(err, 'admin.queries.resetTemplateFailed');
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
            case 'queryType': return item.queryType ?? 0;
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
            text: string; status: 'all' | 'active' | 'disabled'; type: QueryType | 'all';
            group: string; dbUser: string;
          };
          if (f.status === 'active' && !data.isEnabled) return false;
          if (f.status === 'disabled' && data.isEnabled) return false;
          if (f.type !== 'all' && data.queryType !== f.type) return false;
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
          return haystack.includes(f.text.toLowerCase());
        };
        this.refreshFilter();
        this.loading = false;
        this.cdr.detectChanges();
        // Bind paginator/sort after the *ngIf table has rendered
        setTimeout(() => {
          // Both must be set *before* the dataSource takes them: MatTableDataSource reads
          // active/direction and pageIndex when it subscribes, so assigning afterwards would
          // render page 1 unsorted and only correct itself on the next user interaction.
          if (this.restored) {
            this.sort.active = this.restored.sortActive;
            this.sort.direction = this.restored.sortDirection;
          }
          this.dataSource.sort = this.sort;

          const wasRestoring = !!this.restored;
          if (this.restored) {
            // Rows may have been deleted while the admin was away; an out-of-range index
            // renders an empty table with no obvious way back.
            const pageCount = Math.ceil(this.dataSource.filteredData.length / this.paginator.pageSize);
            this.paginator.pageIndex = Math.max(0, Math.min(this.restored.pageIndex, pageCount - 1));
            this.restored = null;
          }
          this.dataSource.paginator = this.paginator;

          // refreshFilter() already saved once above, while paginator/sort were still unbound —
          // i.e. as page 0, no sort. Write the real position back now, or the *second* trip to
          // the edit page and back would come home to page 1.
          if (wasRestoring) this.saveState();

          // Position changes bypass refreshFilter(), so persist them at their own source.
          this.paginator.page.subscribe(() => this.saveState());
          this.sort.sortChange.subscribe(() => this.saveState());
          this.cdr.detectChanges();
        });
      },
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'admin.queries.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  applyFilter(event: Event): void {
    this.textFilter = (event.target as HTMLInputElement).value.trim();
    this.refreshFilter();
  }

  isWriteType(type: QueryType): boolean {
    return isWriteQueryType(type);
  }

  typeLabel(type: QueryType): string {
    return QUERY_TYPE_LABELS[type] ?? QUERY_TYPE_LABELS[QueryType.Other];
  }

  refreshFilter(): void {
    this.dataSource.filter = JSON.stringify({
      text: this.textFilter,
      status: this.statusFilter,
      type: this.typeFilter,
      group: this.groupFilter,
      dbUser: this.dbUserFilter
    });
    // Every filter control routes through here, so this is the single save point for them.
    this.saveState();
    if (this.dataSource.paginator) this.dataSource.paginator.firstPage();
  }

  /** True when anything is narrowing the list — drives the Clear filters button. */
  get hasActiveFilters(): boolean {
    return !!this.textFilter
      || this.statusFilter !== 'all'
      || this.typeFilter !== 'all'
      || this.groupFilter !== 'all'
      || this.dbUserFilter !== 'all';
  }

  clearFilters(): void {
    this.textFilter = '';
    this.statusFilter = 'all';
    this.typeFilter = 'all';
    this.groupFilter = 'all';
    this.dbUserFilter = 'all';
    this.refreshFilter();
  }

  private saveState(): void {
    this.listState.save<QueryListState>(this.STATE_KEY, {
      text: this.textFilter,
      status: this.statusFilter,
      type: this.typeFilter,
      group: this.groupFilter,
      dbUser: this.dbUserFilter,
      pageIndex: this.dataSource.paginator?.pageIndex ?? 0,
      sortActive: this.sort?.active ?? '',
      sortDirection: this.sort?.direction ?? ''
    });
  }

  private restoreState(): void {
    const saved = this.listState.load<QueryListState>(this.STATE_KEY);
    if (!saved) return;

    this.textFilter = saved.text ?? '';
    this.statusFilter = saved.status ?? 'all';
    this.typeFilter = saved.type ?? 'all';
    this.groupFilter = saved.group ?? 'all';
    this.dbUserFilter = saved.dbUser ?? 'all';
    // Page and sort need the rendered table; applied once the paginator is bound.
    this.restored = saved;
  }

  deleteQuery(id: string, name: string): void {
    this.confirmService.askThen({
      titleKey: 'admin.queries.deleteTitle',
      messageKey: 'admin.queries.deleteMessage',
      params: { name },
      confirmText: this.transloco.translate('common.delete'),
      destructive: true
    }, () => {
      this.queryService.deleteQuery(id).subscribe({
        next: () => {
          this.toast.success('admin.queries.deleted');
          this.loadQueries();
        },
        error: (err) => {
          this.toast.error(err, 'admin.queries.deleteFailed');
        }
      });
    });
  }
}
