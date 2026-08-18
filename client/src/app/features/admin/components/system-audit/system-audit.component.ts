import { ChangeDetectorRef, Component, OnInit, ViewChild } from '@angular/core';
import { MatPaginator, PageEvent } from '@angular/material/paginator';
import { MatTable } from '@angular/material/table';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { TranslocoService } from '@jsverse/transloco';
import { ToastService } from '@core/services/toast.service';
import { QueryService } from '@core/services/query.service';
import { SystemAuditLog } from '@core/models/dynamic-query.model';

/**
 * The administrative audit trail — who created a user, granted a permission, edited a query.
 *
 * <p>Separate from the execution logs: that page answers "what did people run", this one
 * answers "what did people change". Both are the Auditor's, which is why they sit next to
 * each other in the nav.</p>
 */
@Component({
  standalone: false,
  selector: 'app-system-audit',
  template: `
    <div class="container">
      <div class="header">
        <h2>{{ 'admin.audit.title' | transloco }}</h2>
        <button mat-icon-button (click)="load()"
                [matTooltip]="'common.refresh' | transloco"
                [attr.aria-label]="'common.refresh' | transloco">
          <mat-icon>refresh</mat-icon>
        </button>
      </div>

      <p class="page-hint">{{ 'admin.audit.subtitle' | transloco }}</p>

      <mat-card>
        <mat-card-content>
          <div class="table-toolbar">
            <mat-form-field appearance="outline" class="filter-field">
              <mat-label>{{ 'admin.audit.filter' | transloco }}</mat-label>
              <input matInput [value]="search"
                     (input)="onSearch($event)"
                     [attr.placeholder]="'admin.audit.filterPlaceholder' | transloco">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>{{ 'admin.audit.category' | transloco }}</mat-label>
              <mat-select [(value)]="category" (selectionChange)="reload()">
                <mat-option value="">{{ 'common.all' | transloco }}</mat-option>
                <mat-option *ngFor="let c of categories" [value]="c">
                  {{ categoryLabel(c) }}
                </mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>{{ 'admin.audit.outcome' | transloco }}</mat-label>
              <mat-select [(value)]="outcome" (selectionChange)="reload()">
                <mat-option value="">{{ 'common.all' | transloco }}</mat-option>
                <mat-option value="true">{{ 'admin.audit.succeeded' | transloco }}</mat-option>
                <mat-option value="false">{{ 'admin.audit.refused' | transloco }}</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <div *ngIf="!loading && errorMessage" class="error-block">
            <p class="error-text">{{ errorMessage }}</p>
            <button mat-stroked-button (click)="load()">{{ 'common.retry' | transloco }}</button>
          </div>

          <div class="table-wrapper" *ngIf="!loading && !errorMessage">
            <!-- multiTemplateDataRows: without it Material renders exactly one row template per
                   item, so the detail row REPLACES its parent instead of appearing under it. -->
              <table mat-table [dataSource]="entries" class="full-width" multiTemplateDataRows>
              <ng-container matColumnDef="occurredAt">
                <th mat-header-cell *matHeaderCellDef>{{ 'admin.audit.when' | transloco }}</th>
                <td mat-cell *matCellDef="let e" class="force-ltr">
                  {{ e.occurredAt | date:'medium' }}
                </td>
              </ng-container>

              <ng-container matColumnDef="username">
                <th mat-header-cell *matHeaderCellDef>{{ 'admin.audit.who' | transloco }}</th>
                <td mat-cell *matCellDef="let e" dir="auto">{{ e.username }}</td>
              </ng-container>

              <ng-container matColumnDef="action">
                <th mat-header-cell *matHeaderCellDef>{{ 'admin.audit.action' | transloco }}</th>
                <td mat-cell *matCellDef="let e">{{ actionLabel(e.action) }}</td>
              </ng-container>

              <ng-container matColumnDef="entityName">
                <th mat-header-cell *matHeaderCellDef>{{ 'admin.audit.target' | transloco }}</th>
                <td mat-cell *matCellDef="let e" dir="auto">
                  <span *ngIf="e.entityName; else noTarget">{{ e.entityName }}</span>
                  <ng-template #noTarget><span class="no-value">&mdash;</span></ng-template>
                </td>
              </ng-container>

              <ng-container matColumnDef="outcome">
                <th mat-header-cell *matHeaderCellDef>{{ 'admin.audit.outcome' | transloco }}</th>
                <td mat-cell *matCellDef="let e">
                  <span class="status"
                        [class.status-Succeeded]="e.isSuccess"
                        [class.status-Failed]="!e.isSuccess">
                    {{ (e.isSuccess ? 'admin.audit.succeeded' : 'admin.audit.refused') | transloco }}
                  </span>
                </td>
              </ng-container>

              <ng-container matColumnDef="details">
                <th mat-header-cell *matHeaderCellDef>{{ 'admin.audit.details' | transloco }}</th>
                <td mat-cell *matCellDef="let e">
                  <!-- The payload is JSON and often contains identifiers, so it is pinned LTR
                       whatever the page direction. -->
                  <button mat-icon-button *ngIf="e.detailsJson || e.errorMessage"
                          (click)="toggle(e)"
                          [matTooltip]="'admin.audit.showDetails' | transloco"
                          [attr.aria-label]="'admin.audit.showDetails' | transloco"
                          [attr.aria-expanded]="expandedId === e.id">
                    <mat-icon>{{ expandedId === e.id ? 'expand_less' : 'expand_more' }}</mat-icon>
                  </button>
                </td>
              </ng-container>

              <ng-container matColumnDef="expanded">
                <td mat-cell *matCellDef="let e" [attr.colspan]="displayedColumns.length">
                  <div class="detail-panel" *ngIf="expandedId === e.id">
                    <p *ngIf="e.errorMessage" class="error-text" dir="auto">{{ e.errorMessage }}</p>
                    <pre *ngIf="e.detailsJson" class="force-ltr">{{ pretty(e.detailsJson) }}</pre>
                    <p class="meta force-ltr" *ngIf="e.ipAddress">{{ 'admin.audit.ipAddress' | transloco }} {{ e.ipAddress }}</p>
                  </div>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
              <tr mat-row *matRowDef="let row; columns: ['expanded']; when: isExpanded"
                  class="detail-row"></tr>

              <tr class="mat-row no-data-row" *matNoDataRow>
                <td class="mat-cell no-data-cell" [attr.colspan]="displayedColumns.length">
                  {{ 'admin.audit.none' | transloco }}
                </td>
              </tr>
            </table>
          </div>

          <mat-paginator *ngIf="!loading && !errorMessage"
                         [length]="totalCount"
                         [pageSize]="pageSize"
                         [pageIndex]="pageIndex"
                         [pageSizeOptions]="[25, 50, 100]"
                         (page)="onPage($event)"
                         showFirstLastButtons>
          </mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    table { width: 100%; }
    .full-width { width: 100%; }
    .filter-field { flex: 1 1 320px; }
    .page-hint { color: var(--text-secondary); margin-top: -8px; }
    .no-value { color: var(--text-secondary); }
    .status { font-weight: 500; }
    .detail-row td { padding: 0; border-bottom-width: 0; }
    .detail-panel {
      padding: 12px 16px;
      background: var(--bg-surface);
      border-inline-start: 3px solid var(--accent-primary);
    }
    .detail-panel pre {
      margin: 0;
      white-space: pre-wrap;
      word-break: break-word;
      font-size: 12px;
      color: var(--text-primary);
    }
    .meta { font-size: 12px; color: var(--text-secondary); margin: 8px 0 0; }
  `]
})
export class SystemAuditComponent implements OnInit {
  @ViewChild(MatPaginator) paginator?: MatPaginator;
  @ViewChild(MatTable) table?: MatTable<SystemAuditLog>;

  displayedColumns = ['occurredAt', 'username', 'action', 'entityName', 'outcome', 'details'];
  entries: SystemAuditLog[] = [];
  categories: string[] = [];

  loading = true;
  errorMessage = '';
  search = '';
  category = '';
  outcome = '';
  expandedId: string | null = null;

  totalCount = 0;
  pageIndex = 0;
  pageSize = 25;

  /** Typing in the filter should not fire a request per keystroke. */
  private searchChanged = new Subject<string>();

  constructor(
    private queryService: QueryService,
    private toast: ToastService,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.searchChanged.pipe(debounceTime(350), distinctUntilChanged()).subscribe(term => {
      this.search = term;
      this.reload();
    });

    this.queryService.getAuditActionCatalog().subscribe({
      next: (catalog) => {
        this.categories = catalog.categories;
        this.cdr.detectChanges();
      },
      // A missing catalog only costs the dropdown; the table itself still works.
      error: () => this.cdr.detectChanges()
    });

    this.load();
  }

  load(): void {
    this.loading = true;
    this.errorMessage = '';

    this.queryService.getSystemAuditLogs({
      search: this.search || undefined,
      category: this.category || undefined,
      isSuccess: this.outcome === '' ? undefined : this.outcome === 'true',
      pageNumber: this.pageIndex + 1,
      pageSize: this.pageSize
    }).subscribe({
      next: (result) => {
        this.entries = result.items;
        this.totalCount = result.totalCount;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || this.transloco.translate('admin.audit.loadFailed');
        this.toast.error(err, 'admin.audit.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  /** Any filter change restarts at page 1 — staying on page 7 of a smaller result set is empty. */
  reload(): void {
    this.pageIndex = 0;
    this.expandedId = null;
    this.load();
  }

  onSearch(event: Event): void {
    this.searchChanged.next((event.target as HTMLInputElement).value);
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.expandedId = null;
    this.load();
  }

  toggle(entry: SystemAuditLog): void {
    this.expandedId = this.expandedId === entry.id ? null : entry.id;
    // MatTable caches its row outlets, so a `when` predicate is not re-evaluated just
    // because component state changed — the detail row would never appear without this.
    this.table?.renderRows();
    this.cdr.detectChanges();
  }

  isExpanded = (_: number, row: SystemAuditLog): boolean => this.expandedId === row.id;

  /**
   * Turns a stored action code into text. Unknown codes fall through to the raw code rather
   * than rendering blank, so an action added server-side is still legible before it is
   * translated.
   */
  actionLabel(action: string): string {
    const key = `admin.audit.actions.${action}`;
    const translated = this.transloco.translate(key);
    return translated === key ? action : translated;
  }

  categoryLabel(category: string): string {
    const key = `admin.audit.categories.${category}`;
    const translated = this.transloco.translate(key);
    return translated === key ? category : translated;
  }

  /** Formats the stored payload for display; shows it raw if it is not valid JSON. */
  pretty(json: string): string {
    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }
}
