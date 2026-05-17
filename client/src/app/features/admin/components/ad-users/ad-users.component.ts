import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatSort } from '@angular/material/sort';
import { MatPaginator } from '@angular/material/paginator';
import { MatTableDataSource } from '@angular/material/table';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { LdapUser, ImportedLdapUser } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-ad-users',
  template: `
    <div class="container">
      <h2>Active Directory User Management</h2>

      <mat-tab-group>
        <!-- Tab 1: Search & Import Users -->
        <mat-tab label="Search Users">
          <div class="tab-content">
            <mat-form-field class="full-width" appearance="outline">
              <mat-label>Search AD users</mat-label>
              <input matInput [(ngModel)]="searchTerm" (keyup.enter)="searchUsers()"
                     placeholder="Username, name, or email">
              <button mat-icon-button matSuffix (click)="searchUsers()" [disabled]="searching">
                <mat-icon>search</mat-icon>
              </button>
            </mat-form-field>

            <div *ngIf="searching" class="loading">
              <mat-spinner diameter="40"></mat-spinner>
            </div>

            <div *ngIf="!searching && searchDataSource.data.length > 0" class="table-toolbar">
              <mat-form-field appearance="outline" class="filter-field">
                <mat-label>Filter results</mat-label>
                <input matInput (keyup)="applySearchFilter($event)" placeholder="Filter by name, email, dept...">
                <mat-icon matSuffix>filter_list</mat-icon>
              </mat-form-field>

              <mat-form-field appearance="outline" class="status-filter">
                <mat-label>Import Status</mat-label>
                <mat-select [(value)]="searchStatusFilter" (selectionChange)="refreshSearchFilter()">
                  <mat-option value="all">All</mat-option>
                  <mat-option value="imported">Imported</mat-option>
                  <mat-option value="not-imported">Not imported</mat-option>
                </mat-select>
              </mat-form-field>
            </div>

            <table mat-table [dataSource]="searchDataSource" matSort #searchSort="matSort"
                   *ngIf="!searching && searchDataSource.data.length > 0" class="full-width">
              <ng-container matColumnDef="select">
                <th mat-header-cell *matHeaderCellDef>
                  <mat-checkbox (change)="toggleAllSearch($event.checked)"
                                [checked]="allSearchSelected()"
                                [indeterminate]="someSearchSelected()">
                  </mat-checkbox>
                </th>
                <td mat-cell *matCellDef="let user">
                  <mat-checkbox [(ngModel)]="selectedUsers[user.username]"
                                [disabled]="user.isImported">
                  </mat-checkbox>
                </td>
              </ng-container>

              <ng-container matColumnDef="username">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Username</th>
                <td mat-cell *matCellDef="let user">{{ user.username }}</td>
              </ng-container>

              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th>
                <td mat-cell *matCellDef="let user">{{ user.firstName }} {{ user.lastName }}</td>
              </ng-container>

              <ng-container matColumnDef="email">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Email</th>
                <td mat-cell *matCellDef="let user">{{ user.email }}</td>
              </ng-container>

              <ng-container matColumnDef="department">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Department</th>
                <td mat-cell *matCellDef="let user">{{ user.department }}</td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
                <td mat-cell *matCellDef="let user">
                  <mat-chip-listbox>
                    <mat-chip [class.imported]="user.isImported">
                      {{ user.isImported ? 'Imported' : 'Not imported' }}
                    </mat-chip>
                  </mat-chip-listbox>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="searchColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: searchColumns;"></tr>

              <tr class="mat-row no-data-row" *matNoDataRow>
                <td class="mat-cell no-data-cell" [attr.colspan]="searchColumns.length">
                  No users match the current filters.
                </td>
              </tr>
            </table>

            <mat-paginator #searchPaginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons
                           *ngIf="!searching && searchDataSource.data.length > 0">
            </mat-paginator>

            <div class="actions" *ngIf="!searching && searchDataSource.data.length > 0">
              <button mat-raised-button color="primary" (click)="importSelectedUsers()"
                      [disabled]="!hasSelectedUsers() || importing">
                <mat-icon>person_add</mat-icon>
                {{ importing ? 'Importing...' : 'Import Selected Users' }}
              </button>
            </div>
          </div>
        </mat-tab>

        <!-- Tab 2: Import by Department -->
        <mat-tab label="Departments">
          <div class="tab-content">
            <div *ngIf="loadingDepts" class="loading">
              <mat-spinner diameter="40"></mat-spinner>
            </div>

            <mat-form-field appearance="outline" class="filter-field" *ngIf="!loadingDepts && departments.length">
              <mat-label>Filter departments</mat-label>
              <input matInput [(ngModel)]="departmentFilter" placeholder="Search departments...">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>

            <div class="dept-grid" *ngIf="!loadingDepts">
              <mat-card *ngFor="let dept of filteredDepartments()" class="dept-card">
                <mat-card-header>
                  <mat-card-title>{{ dept }}</mat-card-title>
                </mat-card-header>
                <mat-card-actions>
                  <button mat-button (click)="viewDepartment(dept)">
                    <mat-icon>people</mat-icon> View Users
                  </button>
                  <button mat-raised-button color="primary" (click)="importDepartment(dept)"
                          [disabled]="importing">
                    <mat-icon>group_add</mat-icon> Import All
                  </button>
                </mat-card-actions>
              </mat-card>
            </div>

            <p *ngIf="!loadingDepts && departments.length > 0 && filteredDepartments().length === 0"
               class="no-data">No departments match "{{ departmentFilter }}".</p>

            <!-- Department users list -->
            <div *ngIf="selectedDepartment" class="dept-users">
              <h3>{{ selectedDepartment }} Users</h3>

              <div *ngIf="deptDataSource.data.length > 0" class="table-toolbar">
                <mat-form-field appearance="outline" class="filter-field">
                  <mat-label>Filter users</mat-label>
                  <input matInput (keyup)="applyDeptFilter($event)" placeholder="Search by name, email...">
                  <mat-icon matSuffix>filter_list</mat-icon>
                </mat-form-field>
              </div>

              <table mat-table [dataSource]="deptDataSource" matSort #deptSort="matSort" class="full-width"
                     *ngIf="deptDataSource.data.length > 0">
                <ng-container matColumnDef="username">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>Username</th>
                  <td mat-cell *matCellDef="let user">{{ user.username }}</td>
                </ng-container>

                <ng-container matColumnDef="name">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th>
                  <td mat-cell *matCellDef="let user">{{ user.firstName }} {{ user.lastName }}</td>
                </ng-container>

                <ng-container matColumnDef="email">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>Email</th>
                  <td mat-cell *matCellDef="let user">{{ user.email }}</td>
                </ng-container>

                <ng-container matColumnDef="status">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
                  <td mat-cell *matCellDef="let user">
                    {{ user.isImported ? 'Imported' : 'Not imported' }}
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="deptColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: deptColumns;"></tr>

                <tr class="mat-row no-data-row" *matNoDataRow>
                  <td class="mat-cell no-data-cell" [attr.colspan]="deptColumns.length">
                    No users match the current filter.
                  </td>
                </tr>
              </table>

              <mat-paginator #deptPaginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons
                             *ngIf="deptDataSource.data.length > 0">
              </mat-paginator>
            </div>
          </div>
        </mat-tab>

        <!-- Tab 3: Imported Users -->
        <mat-tab label="Imported Users">
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
                <mat-label>Filter imported users</mat-label>
                <input matInput (keyup)="applyImportedFilter($event)" placeholder="Search by name, email, dept...">
                <mat-icon matSuffix>filter_list</mat-icon>
              </mat-form-field>

              <mat-form-field appearance="outline" class="status-filter">
                <mat-label>Status</mat-label>
                <mat-select [(value)]="importedStatusFilter" (selectionChange)="refreshImportedFilter()">
                  <mat-option value="all">All</mat-option>
                  <mat-option value="active">Active</mat-option>
                  <mat-option value="revoked">Revoked</mat-option>
                </mat-select>
              </mat-form-field>
            </div>

            <table mat-table [dataSource]="importedDataSource" matSort #importedSort="matSort"
                   *ngIf="!loadingImported && importedDataSource.data.length > 0" class="full-width">
              <ng-container matColumnDef="username">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Username</th>
                <td mat-cell *matCellDef="let user">{{ user.username }}</td>
              </ng-container>

              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Name</th>
                <td mat-cell *matCellDef="let user">{{ user.firstName }} {{ user.lastName }}</td>
              </ng-container>

              <ng-container matColumnDef="email">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Email</th>
                <td mat-cell *matCellDef="let user">{{ user.email }}</td>
              </ng-container>

              <ng-container matColumnDef="department">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Department</th>
                <td mat-cell *matCellDef="let user">{{ user.department }}</td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
                <td mat-cell *matCellDef="let user">
                  <mat-chip-listbox>
                    <mat-chip [class.active]="user.isActive" [class.inactive]="!user.isActive">
                      {{ user.isActive ? 'Active' : 'Revoked' }}
                    </mat-chip>
                  </mat-chip-listbox>
                </td>
              </ng-container>

              <ng-container matColumnDef="actions">
                <th mat-header-cell *matHeaderCellDef>Actions</th>
                <td mat-cell *matCellDef="let user">
                  <button *ngIf="user.isActive" mat-icon-button color="warn"
                          (click)="revokeUser(user.username)" matTooltip="Revoke access">
                    <mat-icon>block</mat-icon>
                  </button>
                  <button *ngIf="!user.isActive" mat-icon-button color="primary"
                          (click)="restoreUser(user.username)" matTooltip="Restore access">
                    <mat-icon>lock_open</mat-icon>
                  </button>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="importedColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: importedColumns;"></tr>

              <tr class="mat-row no-data-row" *matNoDataRow>
                <td class="mat-cell no-data-cell" [attr.colspan]="importedColumns.length">
                  No imported users match the current filters.
                </td>
              </tr>
            </table>

            <mat-paginator #importedPaginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons
                           *ngIf="!loadingImported && importedDataSource.data.length > 0">
            </mat-paginator>

            <p *ngIf="!loadingImported && importedDataSource.data.length === 0" class="no-data">
              No LDAP users have been imported yet.
            </p>
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .tab-content { padding: 24px 0; }
    .tab-header-row { display: flex; justify-content: flex-end; margin-bottom: 12px; }
    .loading { display: flex; justify-content: center; padding: 40px; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 16px; }
    .table-toolbar { display: flex; gap: 12px; align-items: flex-start; margin-bottom: 8px; }
    .filter-field { flex: 1; min-width: 240px; }
    .status-filter { width: 180px; }
    .dept-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px; }
    .dept-card { cursor: pointer; }
    .dept-users { margin-top: 24px; }
    .no-data { text-align: center; color: var(--text-secondary); padding: 40px; }
    .no-data-row { height: 56px; }
    .no-data-cell { text-align: center; color: var(--text-secondary); padding: 16px; }
    .imported { background-color: var(--chip-imported) !important; color: var(--chip-text) !important; }
    .active { background-color: var(--chip-active) !important; color: var(--chip-text) !important; }
    .inactive { background-color: var(--chip-inactive) !important; color: var(--chip-text) !important; }
    table { margin-top: 16px; }
  `]
})
export class AdUsersComponent implements OnInit {
  searchTerm = '';
  searching = false;
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
    private snackBar: MatSnackBar,
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
    if (!this.searchTerm || this.searchTerm.length < 2) return;
    this.searching = true;
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
      error: () => {
        this.snackBar.open('Failed to search AD users', 'Close', { duration: 5000 });
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

  importSelectedUsers(): void {
    const usernames = Object.entries(this.selectedUsers)
      .filter(([_, selected]) => selected)
      .map(([username]) => username);

    if (usernames.length === 0) return;

    this.importing = true;
    this.queryService.importLdapUsers(usernames).subscribe({
      next: (result) => {
        this.snackBar.open(`Imported ${result.imported} user(s)`, 'Close', { duration: 3000 });
        this.importing = false;
        this.selectedUsers = {};
        this.searchUsers();
        this.loadImportedUsers();
      },
      error: () => {
        this.snackBar.open('Failed to import users', 'Close', { duration: 5000 });
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
      error: () => {
        this.snackBar.open('Failed to load departments', 'Close', { duration: 5000 });
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
      error: () => this.snackBar.open('Failed to load department users', 'Close', { duration: 5000 })
    });
  }

  importDepartment(dept: string): void {
    this.importing = true;
    this.queryService.importLdapDepartment(dept).subscribe({
      next: (result) => {
        this.snackBar.open(`Imported ${result.imported} user(s) from ${dept}`, 'Close', { duration: 3000 });
        this.importing = false;
        this.loadImportedUsers();
        if (this.selectedDepartment === dept) {
          this.viewDepartment(dept);
        }
      },
      error: () => {
        this.snackBar.open('Failed to import department', 'Close', { duration: 5000 });
        this.importing = false;
      }
    });
  }

  loadImportedUsers(): void {
    this.loadingImported = true;
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
      error: () => {
        this.loadingImported = false;
        this.cdr.detectChanges();
      }
    });
  }

  revokeUser(username: string): void {
    this.queryService.revokeLdapUser(username).subscribe({
      next: () => {
        this.snackBar.open(`Access revoked for ${username}`, 'Close', { duration: 3000 });
        this.loadImportedUsers();
      },
      error: () => {
        this.snackBar.open('Failed to revoke access', 'Close', { duration: 5000 });
      }
    });
  }

  restoreUser(username: string): void {
    this.queryService.restoreLdapUser(username).subscribe({
      next: () => {
        this.snackBar.open(`Access restored for ${username}`, 'Close', { duration: 3000 });
        this.loadImportedUsers();
      },
      error: () => {
        this.snackBar.open('Failed to restore access', 'Close', { duration: 5000 });
      }
    });
  }

  syncFromAd(): void {
    this.syncing = true;
    this.queryService.syncLdapImportedUsers().subscribe({
      next: (result) => {
        this.syncing = false;
        this.snackBar.open(
          `Sync complete — ${result.synced} updated, ${result.notFound} not found in AD`,
          'Close', { duration: 5000 }
        );
        this.loadImportedUsers();
      },
      error: () => {
        this.syncing = false;
        this.snackBar.open('Sync failed', 'Close', { duration: 5000 });
      }
    });
  }
}
