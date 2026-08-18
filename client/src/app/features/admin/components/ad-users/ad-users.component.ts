import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { MatSort } from '@angular/material/sort';
import { MatPaginator } from '@angular/material/paginator';
import { MatTableDataSource } from '@angular/material/table';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { LdapUser, ImportedLdapUser, LdapImportResult } from '@core/models/dynamic-query.model';
import { TranslocoService } from '@jsverse/transloco';

@Component({
  standalone: false,
  selector: 'app-ad-users',
  template: `
    <div class="container">
      <h2>{{ 'admin.adUsers.title' | transloco }}</h2>

      <mat-tab-group>
        <!-- Tab 1: Search & Import Users -->
        <mat-tab [label]="'admin.adUsers.searchTab' | transloco">
          <div class="tab-content">
            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.adUsers.search' | transloco }}</mat-label>
              <input matInput [(ngModel)]="searchTerm" (keyup.enter)="searchUsers()"
                     [attr.placeholder]="'admin.adUsers.searchPlaceholder' | transloco">
              <button mat-icon-button matSuffix (click)="searchUsers()" [disabled]="searching"
                      [matTooltip]="'admin.adUsers.searchAction' | transloco" [attr.aria-label]="'admin.adUsers.search' | transloco">
                <mat-icon>search</mat-icon>
              </button>
            </mat-form-field>

            <div *ngIf="searching" class="loading">
              <mat-spinner diameter="40"></mat-spinner>
            </div>

            <div *ngIf="!searching && searchDataSource.data.length > 0" class="table-toolbar">
              <mat-form-field appearance="outline" class="filter-field">
                <mat-label>{{ 'admin.adUsers.filterResults' | transloco }}</mat-label>
                <input matInput (keyup)="applySearchFilter($event)" [attr.placeholder]="'admin.adUsers.filterPlaceholder' | transloco">
                <mat-icon matSuffix>filter_list</mat-icon>
              </mat-form-field>

              <mat-form-field appearance="outline" class="status-filter">
                <mat-label>{{ 'admin.adUsers.importStatus' | transloco }}</mat-label>
                <mat-select [(value)]="searchStatusFilter" (selectionChange)="refreshSearchFilter()">
                  <mat-option value="all">{{ 'common.all' | transloco }}</mat-option>
                  <mat-option value="imported">{{ 'admin.adUsers.imported' | transloco }}</mat-option>
                  <mat-option value="not-imported">{{ 'admin.adUsers.notImported' | transloco }}</mat-option>
                </mat-select>
              </mat-form-field>
            </div>

            <div class="table-wrapper">
            <table mat-table [dataSource]="searchDataSource" matSort #searchSort="matSort"
                   *ngIf="!searching && searched" class="full-width">
              <ng-container matColumnDef="select">
                <th mat-header-cell *matHeaderCellDef>
                  <mat-checkbox (change)="toggleAllSearch($event.checked)"
                                [checked]="allSearchSelected()"
                                [indeterminate]="someSearchSelected()"
                                [attr.aria-label]="'admin.adUsers.selectAll' | transloco">
                  </mat-checkbox>
                </th>
                <td mat-cell *matCellDef="let user">
                  <mat-checkbox [(ngModel)]="selectedUsers[user.username]"
                                [disabled]="user.isImported"
                                [attr.aria-label]="'Select ' + user.username">
                  </mat-checkbox>
                </td>
              </ng-container>

              <ng-container matColumnDef="username">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.username' | transloco }}</th>
                <td mat-cell *matCellDef="let user">{{ user.username }}</td>
              </ng-container>

              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.name' | transloco }}</th>
                <td mat-cell *matCellDef="let user">{{ user.firstName }} {{ user.lastName }}</td>
              </ng-container>

              <ng-container matColumnDef="email">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.email' | transloco }}</th>
                <td mat-cell *matCellDef="let user">{{ user.email }}</td>
              </ng-container>

              <ng-container matColumnDef="department">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.department' | transloco }}</th>
                <td mat-cell *matCellDef="let user">{{ user.department }}</td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.status' | transloco }}</th>
                <td mat-cell *matCellDef="let user">
                  <mat-chip-listbox>
                    <mat-chip [class.imported]="user.isImported">
                      {{ (user.isImported ? 'admin.adUsers.imported' : 'admin.adUsers.notImported') | transloco }}
                    </mat-chip>
                  </mat-chip-listbox>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="searchColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: searchColumns;"></tr>

              <tr class="mat-row no-data-row" *matNoDataRow>
                <td class="mat-cell no-data-cell" [attr.colspan]="searchColumns.length">
                  {{ 'admin.adUsers.noSearchMatch' | transloco }}
                </td>
              </tr>
            </table>
            </div>

            <mat-paginator #searchPaginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons
                           *ngIf="!searching && searchDataSource.data.length > 0">
            </mat-paginator>

            <div class="actions" *ngIf="!searching && searchDataSource.data.length > 0">
              <button mat-raised-button color="primary" (click)="importSelectedUsers()"
                      [disabled]="!hasSelectedUsers() || importing">
                <mat-icon>person_add</mat-icon>
                {{ (importing ? 'admin.adUsers.importing' : 'admin.adUsers.importSelected') | transloco }}
              </button>
            </div>
          </div>
        </mat-tab>

        <!-- Tab 2: Import by Department -->
        <mat-tab [label]="'admin.adUsers.departmentsTab' | transloco">
          <div class="tab-content">
            <div *ngIf="loadingDepts" class="loading">
              <mat-spinner diameter="40"></mat-spinner>
            </div>

            <mat-form-field appearance="outline" class="filter-field" *ngIf="!loadingDepts && departments.length">
              <mat-label>{{ 'admin.adUsers.filterDepartments' | transloco }}</mat-label>
              <input matInput [(ngModel)]="departmentFilter" [attr.placeholder]="'admin.adUsers.filterDepartmentsPlaceholder' | transloco">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>

            <div class="dept-grid" *ngIf="!loadingDepts">
              <mat-card *ngFor="let dept of filteredDepartments()" class="dept-card">
                <mat-card-header>
                  <mat-card-title>{{ dept }}</mat-card-title>
                </mat-card-header>
                <mat-card-actions>
                  <button mat-button (click)="viewDepartment(dept)">
                    <mat-icon>people</mat-icon> {{ 'admin.adUsers.viewUsers' | transloco }}
                  </button>
                  <button mat-raised-button color="primary" (click)="importDepartment(dept)"
                          [disabled]="importing">
                    <mat-icon>group_add</mat-icon> {{ 'admin.adUsers.importAll' | transloco }}
                  </button>
                </mat-card-actions>
              </mat-card>
            </div>

            <p *ngIf="!loadingDepts && departments.length > 0 && filteredDepartments().length === 0"
               class="no-data">{{ 'admin.adUsers.noDepartmentMatch' | transloco: { filter: departmentFilter } }}</p>

            <!-- Department users list -->
            <div *ngIf="selectedDepartment" class="dept-users">
              <h3>{{ 'admin.adUsers.departmentUsers' | transloco: { department: selectedDepartment } }}</h3>

              <div *ngIf="deptDataSource.data.length > 0" class="table-toolbar">
                <mat-form-field appearance="outline" class="filter-field">
                  <mat-label>{{ 'admin.users.filter' | transloco }}</mat-label>
                  <input matInput (keyup)="applyDeptFilter($event)" [attr.placeholder]="'admin.adUsers.filterUsersPlaceholder' | transloco">
                  <mat-icon matSuffix>filter_list</mat-icon>
                </mat-form-field>
              </div>

              <div class="table-wrapper">
              <table mat-table [dataSource]="deptDataSource" matSort #deptSort="matSort" class="full-width"
                     *ngIf="deptDataSource.data.length > 0">
                <ng-container matColumnDef="username">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.username' | transloco }}</th>
                  <td mat-cell *matCellDef="let user">{{ user.username }}</td>
                </ng-container>

                <ng-container matColumnDef="name">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.name' | transloco }}</th>
                  <td mat-cell *matCellDef="let user">{{ user.firstName }} {{ user.lastName }}</td>
                </ng-container>

                <ng-container matColumnDef="email">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.email' | transloco }}</th>
                  <td mat-cell *matCellDef="let user">{{ user.email }}</td>
                </ng-container>

                <ng-container matColumnDef="status">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.status' | transloco }}</th>
                  <td mat-cell *matCellDef="let user">
                    {{ (user.isImported ? 'admin.adUsers.imported' : 'admin.adUsers.notImported') | transloco }}
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="deptColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: deptColumns;"></tr>

                <tr class="mat-row no-data-row" *matNoDataRow>
                  <td class="mat-cell no-data-cell" [attr.colspan]="deptColumns.length">
                    {{ 'admin.adUsers.noUserMatch' | transloco }}
                  </td>
                </tr>
              </table>
              </div>

              <mat-paginator #deptPaginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons
                             *ngIf="deptDataSource.data.length > 0">
              </mat-paginator>
            </div>
          </div>
        </mat-tab>

        <!-- Tab 3: Imported Users -->
        <mat-tab [label]="'admin.adUsers.importedTab' | transloco">
          <div class="tab-content">
            <div class="tab-header-row">
              <button mat-stroked-button (click)="syncFromAd()" [disabled]="syncing || loadingImported">
                <mat-icon>sync</mat-icon>
                {{ syncing ? 'Syncing...' : 'Sync from AD' }}
              </button>
            </div>

            <div *ngIf="loadingImported" class="loading">
              <mat-spinner diameter="40"></mat-spinner>
            </div>

            <div *ngIf="!loadingImported && importedDataSource.data.length > 0" class="table-toolbar">
              <mat-form-field appearance="outline" class="filter-field">
                <mat-label>{{ 'admin.adUsers.filterImported' | transloco }}</mat-label>
                <input matInput (keyup)="applyImportedFilter($event)" [attr.placeholder]="'admin.adUsers.filterPlaceholder' | transloco">
                <mat-icon matSuffix>filter_list</mat-icon>
              </mat-form-field>

              <mat-form-field appearance="outline" class="status-filter">
                <mat-label>{{ 'admin.users.status' | transloco }}</mat-label>
                <mat-select [(value)]="importedStatusFilter" (selectionChange)="refreshImportedFilter()">
                  <mat-option value="all">{{ 'common.all' | transloco }}</mat-option>
                  <mat-option value="active">{{ 'common.active' | transloco }}</mat-option>
                  <mat-option value="revoked">{{ 'admin.adUsers.statusRevoked' | transloco }}</mat-option>
                </mat-select>
              </mat-form-field>
            </div>

            <div class="table-wrapper">
            <table mat-table [dataSource]="importedDataSource" matSort #importedSort="matSort"
                   *ngIf="!loadingImported && importedDataSource.data.length > 0" class="full-width">
              <ng-container matColumnDef="username">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.username' | transloco }}</th>
                <td mat-cell *matCellDef="let user">{{ user.username }}</td>
              </ng-container>

              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.name' | transloco }}</th>
                <td mat-cell *matCellDef="let user">{{ user.firstName }} {{ user.lastName }}</td>
              </ng-container>

              <ng-container matColumnDef="email">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.email' | transloco }}</th>
                <td mat-cell *matCellDef="let user">{{ user.email }}</td>
              </ng-container>

              <ng-container matColumnDef="department">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.department' | transloco }}</th>
                <td mat-cell *matCellDef="let user">{{ user.department }}</td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'admin.users.status' | transloco }}</th>
                <td mat-cell *matCellDef="let user">
                  <mat-chip-listbox>
                    <mat-chip [class.active]="user.isActive" [class.inactive]="!user.isActive">
                      {{ user.isActive ? 'Active' : 'Revoked' }}
                    </mat-chip>
                  </mat-chip-listbox>
                </td>
              </ng-container>

              <ng-container matColumnDef="actions">
                <th mat-header-cell *matHeaderCellDef>{{ 'common.actions' | transloco }}</th>
                <td mat-cell *matCellDef="let user">
                  <button *ngIf="user.isActive" mat-icon-button color="warn"
                          (click)="revokeUser(user.username)" [matTooltip]="'admin.adUsers.revokeAction' | transloco"
                          [attr.aria-label]="'Revoke access for ' + user.username">
                    <mat-icon>block</mat-icon>
                  </button>
                  <button *ngIf="!user.isActive" mat-icon-button color="primary"
                          (click)="restoreUser(user.username)" [matTooltip]="'admin.adUsers.restoreAction' | transloco"
                          [attr.aria-label]="'Restore access for ' + user.username">
                    <mat-icon>lock_open</mat-icon>
                  </button>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="importedColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: importedColumns;"></tr>

              <tr class="mat-row no-data-row" *matNoDataRow>
                <td class="mat-cell no-data-cell" [attr.colspan]="importedColumns.length">
                  {{ 'admin.adUsers.noImportedMatch' | transloco }}
                </td>
              </tr>
            </table>
            </div>

            <mat-paginator #importedPaginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons
                           *ngIf="!loadingImported && importedDataSource.data.length > 0">
            </mat-paginator>

            <p *ngIf="!loadingImported && importedDataSource.data.length === 0 && !importedLoadFailed"
               class="no-data">
              {{ 'admin.adUsers.noneImported' | transloco }}
            </p>
            <div *ngIf="!loadingImported && importedLoadFailed" class="error-block">
              <p class="error-text">{{ 'admin.adUsers.importedLoadFailed' | transloco }}</p>
              <button mat-stroked-button (click)="loadImportedUsers()">{{ 'common.retry' | transloco }}</button>
            </div>
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .tab-content { padding: 24px 0; }
    .tab-header-row { display: flex; justify-content: flex-end; margin-bottom: 12px; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 16px; }
    .filter-field { flex: 1; min-width: 240px; }
    .status-filter { width: 180px; }
    .dept-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px; }
    .dept-card { cursor: pointer; }
    .dept-users { margin-top: 24px; }
    .no-data { text-align: center; color: var(--text-secondary); padding: 40px; }
    .imported { background-color: var(--chip-imported) !important; color: var(--chip-text) !important; }
    .active { background-color: var(--chip-active) !important; color: var(--chip-text) !important; }
    .inactive { background-color: var(--chip-inactive) !important; color: var(--chip-text) !important; }
    table { margin-top: 16px; }
  `]
})
export class AdUsersComponent implements OnInit {
  searchTerm = '';
  searching = false;
  /** True once a search has run, so a zero-result search can show the no-data row. */
  searched = false;
  /** True when the imported-users fetch failed, to distinguish it from a genuinely empty list. */
  importedLoadFailed = false;
  importing = false;
  syncing = false;
  loadingDepts = false;
  loadingImported = false;

  selectedUsers: Record<string, boolean> = {};
  departments: string[] = [];
  departmentFilter = '';
  selectedDepartment: string | null = null;

  searchDataSource = new MatTableDataSource<LdapUser>();
  deptDataSource = new MatTableDataSource<LdapUser>();
  importedDataSource = new MatTableDataSource<ImportedLdapUser>();

  searchStatusFilter: 'all' | 'imported' | 'not-imported' = 'all';
  importedStatusFilter: 'all' | 'active' | 'revoked' = 'all';
  private searchTextFilter = '';
  private importedTextFilter = '';

  searchColumns = ['select', 'username', 'name', 'email', 'department', 'status'];
  deptColumns = ['username', 'name', 'email', 'status'];
  importedColumns = ['username', 'name', 'email', 'department', 'status', 'actions'];

  @ViewChild('searchSort') searchSort?: MatSort;
  @ViewChild('searchPaginator') searchPaginator?: MatPaginator;
  @ViewChild('deptSort') deptSort?: MatSort;
  @ViewChild('deptPaginator') deptPaginator?: MatPaginator;
  @ViewChild('importedSort') importedSort?: MatSort;
  @ViewChild('importedPaginator') importedPaginator?: MatPaginator;

  constructor(
    private queryService: QueryService,
    private toast: ToastService,
    private confirmService: ConfirmService,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef
  ) {
    this.searchDataSource.sortingDataAccessor = this.ldapSortAccessor as any;
    this.deptDataSource.sortingDataAccessor = this.ldapSortAccessor as any;
    this.importedDataSource.sortingDataAccessor = (item, property) => {
      switch (property) {
        case 'name': return `${item.firstName || ''} ${item.lastName || ''}`.toLowerCase();
        case 'status': return item.isActive ? 1 : 0;
        case 'username': return (item.username || '').toLowerCase();
        case 'email': return (item.email || '').toLowerCase();
        case 'department': return (item.department || '').toLowerCase();
        default: return (item as any)[property];
      }
    };

    this.searchDataSource.filterPredicate = (data, filter) => {
      const f = JSON.parse(filter) as { text: string; status: 'all' | 'imported' | 'not-imported' };
      if (f.status === 'imported' && !data.isImported) return false;
      if (f.status === 'not-imported' && data.isImported) return false;
      if (!f.text) return true;
      return this.userHaystack(data).includes(f.text);
    };

    this.deptDataSource.filterPredicate = (data, filter) => {
      if (!filter) return true;
      return this.userHaystack(data).includes(filter);
    };

    this.importedDataSource.filterPredicate = (data, filter) => {
      const f = JSON.parse(filter) as { text: string; status: 'all' | 'active' | 'revoked' };
      if (f.status === 'active' && !data.isActive) return false;
      if (f.status === 'revoked' && data.isActive) return false;
      if (!f.text) return true;
      return this.userHaystack(data).includes(f.text);
    };
  }

  private ldapSortAccessor(item: LdapUser, property: string): string | number {
    switch (property) {
      case 'name': return `${item.firstName || ''} ${item.lastName || ''}`.toLowerCase();
      case 'status': return item.isImported ? 1 : 0;
      case 'username': return (item.username || '').toLowerCase();
      case 'email': return (item.email || '').toLowerCase();
      case 'department': return (item.department || '').toLowerCase();
      default: return (item as any)[property];
    }
  }

  private userHaystack(u: { username?: string; firstName?: string; lastName?: string; email?: string; department?: string }): string {
    return [u.username, u.firstName, u.lastName, u.email, u.department].filter(Boolean).join(' ').toLowerCase();
  }

  filteredDepartments(): string[] {
    const f = (this.departmentFilter || '').trim().toLowerCase();
    if (!f) return this.departments;
    return this.departments.filter(d => d.toLowerCase().includes(f));
  }

  ngOnInit(): void {
    this.loadDepartments();
    this.loadImportedUsers();
  }

  applySearchFilter(event: Event): void {
    this.searchTextFilter = (event.target as HTMLInputElement).value.trim().toLowerCase();
    this.refreshSearchFilter();
  }

  refreshSearchFilter(): void {
    this.searchDataSource.filter = JSON.stringify({ text: this.searchTextFilter, status: this.searchStatusFilter });
    if (this.searchDataSource.paginator) this.searchDataSource.paginator.firstPage();
  }

  applyDeptFilter(event: Event): void {
    this.deptDataSource.filter = (event.target as HTMLInputElement).value.trim().toLowerCase();
    if (this.deptDataSource.paginator) this.deptDataSource.paginator.firstPage();
  }

  applyImportedFilter(event: Event): void {
    this.importedTextFilter = (event.target as HTMLInputElement).value.trim().toLowerCase();
    this.refreshImportedFilter();
  }

  refreshImportedFilter(): void {
    this.importedDataSource.filter = JSON.stringify({ text: this.importedTextFilter, status: this.importedStatusFilter });
    if (this.importedDataSource.paginator) this.importedDataSource.paginator.firstPage();
  }

  searchUsers(): void {
    if (!this.searchTerm || this.searchTerm.length < 2) {
      this.toast.info('admin.adUsers.minSearchLength');
      return;
    }
    this.searching = true;
    this.searched = true;
    this.selectedUsers = {};
    this.queryService.searchLdapUsers(this.searchTerm).pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Search timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (users) => {
        this.searchDataSource.data = users;
        this.searching = false;
        this.cdr.detectChanges();
        // Bind after view renders the table
        setTimeout(() => {
          if (this.searchSort) this.searchDataSource.sort = this.searchSort;
          if (this.searchPaginator) this.searchDataSource.paginator = this.searchPaginator;
          this.refreshSearchFilter();
        });
      },
      error: (err) => {
        this.toast.error(err, 'admin.adUsers.searchFailed');
        this.searching = false;
        this.cdr.detectChanges();
      }
    });
  }

  toggleAllSearch(checked: boolean): void {
    this.searchDataSource.data.forEach(u => {
      if (!u.isImported) {
        this.selectedUsers[u.username] = checked;
      }
    });
  }

  allSearchSelected(): boolean {
    const selectable = this.searchDataSource.data.filter(u => !u.isImported);
    return selectable.length > 0 && selectable.every(u => this.selectedUsers[u.username]);
  }

  someSearchSelected(): boolean {
    const selectable = this.searchDataSource.data.filter(u => !u.isImported);
    const selected = selectable.filter(u => this.selectedUsers[u.username]);
    return selected.length > 0 && selected.length < selectable.length;
  }

  hasSelectedUsers(): boolean {
    return Object.values(this.selectedUsers).some(v => v);
  }

  /**
   * Shows the outcome of an import. Anything less than a clean run goes out as info rather
   * than success — "0 imported, 3 skipped" is not a success, and styling it as one is how
   * a failed import used to pass unnoticed.
   */
  private reportImport(result: LdapImportResult, department?: string): void {
    const scope = department ? ` from ${department}` : '';
    const message = `${result.summary}`.trim();
    const clean = result.imported > 0 && result.skipped.length === 0 && result.notFound.length === 0;

    if (clean) {
      this.toast.success('admin.adUsers.importedCount', { count: result.imported, scope });
    } else {
      this.toast.info('admin.adUsers.importOutcome', { scope, message });
    }
  }

  importSelectedUsers(): void {
    const usernames = Object.entries(this.selectedUsers)
      .filter(([_, selected]) => selected)
      .map(([username]) => username);

    if (usernames.length === 0) return;

    this.importing = true;
    this.queryService.importLdapUsers(usernames).subscribe({
      next: (result) => {
        // The summary carries the per-user reasons; a bare count hid why an import
        // that reported success actually brought in nothing.
        this.reportImport(result);
        this.importing = false;
        this.selectedUsers = {};
        this.searchUsers();
        this.loadImportedUsers();
      },
      error: (err) => {
        this.toast.error(err, 'admin.adUsers.importFailed');
        this.importing = false;
      }
    });
  }

  loadDepartments(): void {
    this.loadingDepts = true;
    this.queryService.getLdapDepartments().pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (depts) => {
        this.departments = depts;
        this.loadingDepts = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.toast.error(err, 'admin.adUsers.departmentsLoadFailed');
        this.loadingDepts = false;
        this.cdr.detectChanges();
      }
    });
  }

  viewDepartment(dept: string): void {
    this.selectedDepartment = dept;
    this.queryService.getLdapDepartmentUsers(dept).subscribe({
      next: (users) => {
        this.deptDataSource.data = users;
        this.cdr.detectChanges();
        setTimeout(() => {
          if (this.deptSort) this.deptDataSource.sort = this.deptSort;
          if (this.deptPaginator) this.deptDataSource.paginator = this.deptPaginator;
        });
      },
      error: (err) => this.toast.error(err, 'admin.adUsers.departmentUsersLoadFailed')
    });
  }

  importDepartment(dept: string): void {
    this.importing = true;
    this.queryService.importLdapDepartment(dept).subscribe({
      next: (result) => {
        this.reportImport(result, dept);
        this.importing = false;
        this.loadImportedUsers();
        if (this.selectedDepartment === dept) {
          this.viewDepartment(dept);
        }
      },
      error: (err) => {
        this.toast.error(err, 'admin.adUsers.importDepartmentFailed');
        this.importing = false;
      }
    });
  }

  loadImportedUsers(): void {
    this.loadingImported = true;
    this.importedLoadFailed = false;
    this.queryService.getImportedLdapUsers().pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (users) => {
        this.importedDataSource.data = users;
        this.loadingImported = false;
        this.cdr.detectChanges();
        setTimeout(() => {
          if (this.importedSort) this.importedDataSource.sort = this.importedSort;
          if (this.importedPaginator) this.importedDataSource.paginator = this.importedPaginator;
          this.refreshImportedFilter();
        });
      },
      // Without surfacing this, a failed load was indistinguishable from
      // "no LDAP users have been imported yet".
      error: (err) => {
        this.loadingImported = false;
        this.importedLoadFailed = true;
        this.toast.error(err, 'admin.adUsers.importedLoadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  revokeUser(username: string): void {
    this.confirmService.askThen({
      titleKey: 'admin.adUsers.revokeTitle',
      messageKey: 'admin.adUsers.revokeMessage',
      params: { username },
      confirmText: this.transloco.translate('admin.adUsers.revokeConfirm'),
      destructive: true
    }, () => this.doRevoke(username));
  }

  private doRevoke(username: string): void {
    this.queryService.revokeLdapUser(username).subscribe({
      next: () => {
        this.toast.success('admin.adUsers.accessRevoked', { username });
        this.loadImportedUsers();
      },
      error: (err) => {
        this.toast.error(err, 'admin.adUsers.revokeFailed');
      }
    });
  }

  restoreUser(username: string): void {
    this.queryService.restoreLdapUser(username).subscribe({
      next: () => {
        this.toast.success('admin.adUsers.accessRestored', { username });
        this.loadImportedUsers();
      },
      error: (err) => {
        this.toast.error(err, 'admin.adUsers.restoreFailed');
      }
    });
  }

  syncFromAd(): void {
    this.syncing = true;
    this.queryService.syncLdapImportedUsers().subscribe({
      next: (result) => {
        this.syncing = false;
        this.toast.success(
          `Sync complete — ${result.synced} updated, ${result.notFound} not found in AD`,
          5000
        );
        this.loadImportedUsers();
      },
      error: (err) => {
        this.syncing = false;
        this.toast.error(err, 'admin.adUsers.syncFailed');
      }
    });
  }
}
