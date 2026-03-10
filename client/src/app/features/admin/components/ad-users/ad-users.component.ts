import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
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

            <table mat-table [dataSource]="searchResults" *ngIf="searchResults.length > 0" class="full-width">
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
                <th mat-header-cell *matHeaderCellDef>Username</th>
                <td mat-cell *matCellDef="let user">{{ user.username }}</td>
              </ng-container>

              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>Name</th>
                <td mat-cell *matCellDef="let user">{{ user.firstName }} {{ user.lastName }}</td>
              </ng-container>

              <ng-container matColumnDef="email">
                <th mat-header-cell *matHeaderCellDef>Email</th>
                <td mat-cell *matCellDef="let user">{{ user.email }}</td>
              </ng-container>

              <ng-container matColumnDef="department">
                <th mat-header-cell *matHeaderCellDef>Department</th>
                <td mat-cell *matCellDef="let user">{{ user.department }}</td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef>Status</th>
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
            </table>

            <div class="actions" *ngIf="searchResults.length > 0">
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

            <div class="dept-grid" *ngIf="!loadingDepts">
              <mat-card *ngFor="let dept of departments" class="dept-card">
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

            <!-- Department users list -->
            <div *ngIf="selectedDepartment" class="dept-users">
              <h3>{{ selectedDepartment }} Users</h3>
              <table mat-table [dataSource]="deptUsers" class="full-width">
                <ng-container matColumnDef="username">
                  <th mat-header-cell *matHeaderCellDef>Username</th>
                  <td mat-cell *matCellDef="let user">{{ user.username }}</td>
                </ng-container>

                <ng-container matColumnDef="name">
                  <th mat-header-cell *matHeaderCellDef>Name</th>
                  <td mat-cell *matCellDef="let user">{{ user.firstName }} {{ user.lastName }}</td>
                </ng-container>

                <ng-container matColumnDef="email">
                  <th mat-header-cell *matHeaderCellDef>Email</th>
                  <td mat-cell *matCellDef="let user">{{ user.email }}</td>
                </ng-container>

                <ng-container matColumnDef="status">
                  <th mat-header-cell *matHeaderCellDef>Status</th>
                  <td mat-cell *matCellDef="let user">
                    {{ user.isImported ? 'Imported' : 'Not imported' }}
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="deptColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: deptColumns;"></tr>
              </table>
            </div>
          </div>
        </mat-tab>

        <!-- Tab 3: Imported Users -->
        <mat-tab label="Imported Users">
          <div class="tab-content">
            <div *ngIf="loadingImported" class="loading">
              <mat-spinner diameter="40"></mat-spinner>
            </div>

            <table mat-table [dataSource]="importedUsers" *ngIf="!loadingImported" class="full-width">
              <ng-container matColumnDef="username">
                <th mat-header-cell *matHeaderCellDef>Username</th>
                <td mat-cell *matCellDef="let user">{{ user.username }}</td>
              </ng-container>

              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>Name</th>
                <td mat-cell *matCellDef="let user">{{ user.firstName }} {{ user.lastName }}</td>
              </ng-container>

              <ng-container matColumnDef="email">
                <th mat-header-cell *matHeaderCellDef>Email</th>
                <td mat-cell *matCellDef="let user">{{ user.email }}</td>
              </ng-container>

              <ng-container matColumnDef="department">
                <th mat-header-cell *matHeaderCellDef>Department</th>
                <td mat-cell *matCellDef="let user">{{ user.department }}</td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef>Status</th>
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
                  <button mat-icon-button color="warn" (click)="revokeUser(user.username)"
                          [disabled]="!user.isActive" matTooltip="Revoke access">
                    <mat-icon>block</mat-icon>
                  </button>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="importedColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: importedColumns;"></tr>
            </table>

            <p *ngIf="!loadingImported && importedUsers.length === 0" class="no-data">
              No LDAP users have been imported yet.
            </p>
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .tab-content { padding: 24px 0; }
    .loading { display: flex; justify-content: center; padding: 40px; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 16px; }
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
  importing = false;
  loadingDepts = false;
  loadingImported = false;

  searchResults: LdapUser[] = [];
  selectedUsers: Record<string, boolean> = {};
  departments: string[] = [];
  selectedDepartment: string | null = null;
  deptUsers: LdapUser[] = [];
  importedUsers: ImportedLdapUser[] = [];

  searchColumns = ['select', 'username', 'name', 'email', 'department', 'status'];
  deptColumns = ['username', 'name', 'email', 'status'];
  importedColumns = ['username', 'name', 'email', 'department', 'status', 'actions'];

  constructor(
    private queryService: QueryService,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.loadDepartments();
    this.loadImportedUsers();
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
        this.searchResults = users;
        this.searching = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.snackBar.open('Failed to search AD users', 'Close', { duration: 5000 });
        this.searching = false;
        this.cdr.detectChanges();
      }
    });
  }

  toggleAllSearch(checked: boolean): void {
    this.searchResults.forEach(u => {
      if (!u.isImported) {
        this.selectedUsers[u.username] = checked;
      }
    });
  }

  allSearchSelected(): boolean {
    const selectable = this.searchResults.filter(u => !u.isImported);
    return selectable.length > 0 && selectable.every(u => this.selectedUsers[u.username]);
  }

  someSearchSelected(): boolean {
    const selectable = this.searchResults.filter(u => !u.isImported);
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
      next: (users) => this.deptUsers = users,
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
        this.importedUsers = users;
        this.loadingImported = false;
        this.cdr.detectChanges();
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
}
