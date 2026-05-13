import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableDataSource } from '@angular/material/table';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { SystemUser, Role } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-user-management',
  template: `
    <div class="container">
      <div class="header">
        <h2>User Management</h2>
        <button mat-raised-button color="primary" (click)="showCreateForm = true" *ngIf="!showCreateForm">
          <mat-icon>person_add</mat-icon> Create User
        </button>
      </div>

      <!-- Create User Form -->
      <mat-card *ngIf="showCreateForm" class="form-card">
        <mat-card-header>
          <mat-card-title>Create New User</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form [formGroup]="createForm" (ngSubmit)="createUser()">
            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Username</mat-label>
                <input matInput formControlName="username">
                <mat-error *ngIf="createForm.get('username')?.hasError('required')">Username is required</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Email</mat-label>
                <input matInput formControlName="email" type="email">
                <mat-error *ngIf="createForm.get('email')?.hasError('required')">Email is required</mat-error>
                <mat-error *ngIf="createForm.get('email')?.hasError('email')">Invalid email</mat-error>
              </mat-form-field>
            </div>

            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>First Name</mat-label>
                <input matInput formControlName="firstName">
                <mat-error *ngIf="createForm.get('firstName')?.hasError('required')">First name is required</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Last Name</mat-label>
                <input matInput formControlName="lastName">
                <mat-error *ngIf="createForm.get('lastName')?.hasError('required')">Last name is required</mat-error>
              </mat-form-field>
            </div>

            <div class="form-row">
              <mat-form-field appearance="outline">
                <mat-label>Password</mat-label>
                <input matInput formControlName="password" type="password">
                <mat-error *ngIf="createForm.get('password')?.hasError('required')">Password is required</mat-error>
                <mat-error *ngIf="createForm.get('password')?.hasError('minlength')">Minimum 6 characters</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Department</mat-label>
                <input matInput formControlName="department">
              </mat-form-field>
            </div>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Roles</mat-label>
              <mat-select formControlName="roleIds" multiple>
                <mat-option *ngFor="let role of roles" [value]="role.id">{{ role.name }}</mat-option>
              </mat-select>
              <mat-error *ngIf="createForm.get('roleIds')?.hasError('required')">At least one role is required</mat-error>
            </mat-form-field>

            <div class="form-actions">
              <button mat-button type="button" (click)="cancelCreate()">Cancel</button>
              <button mat-raised-button color="primary" type="submit"
                      [disabled]="createForm.invalid || saving">
                {{ saving ? 'Creating...' : 'Create User' }}
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>

      <!-- Users Table -->
      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <div *ngIf="!loading" class="table-wrapper">
        <mat-form-field appearance="outline" class="filter-field">
          <mat-label>Filter users</mat-label>
          <input matInput (keyup)="applyFilter($event)" placeholder="Search by name, username, email...">
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>

        <table mat-table [dataSource]="dataSource" matSort class="full-width">
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

          <ng-container matColumnDef="roles">
            <th mat-header-cell *matHeaderCellDef>Roles</th>
            <td mat-cell *matCellDef="let user">
              <mat-chip-listbox>
                <mat-chip *ngFor="let role of user.roles" [class.admin-chip]="role === 'Admin'">
                  {{ role }}
                </mat-chip>
              </mat-chip-listbox>
            </td>
          </ng-container>

          <ng-container matColumnDef="authSource">
            <th mat-header-cell *matHeaderCellDef mat-sort-header>Auth Source</th>
            <td mat-cell *matCellDef="let user">{{ user.authSource }}</td>
          </ng-container>

          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
            <td mat-cell *matCellDef="let user">
              <mat-chip-listbox>
                <mat-chip [class.active]="user.isActive" [class.inactive]="!user.isActive">
                  {{ user.isActive ? 'Active' : 'Inactive' }}
                </mat-chip>
              </mat-chip-listbox>
            </td>
          </ng-container>

          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef>Actions</th>
            <td mat-cell *matCellDef="let user">
              <button mat-icon-button [matMenuTriggerFor]="actionMenu" matTooltip="Actions">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #actionMenu="matMenu">
                <button mat-menu-item (click)="openEditUsername(user)"
                        [disabled]="isLdapUser(user)"
                        [matTooltip]="isLdapUser(user) ? 'Managed by Active Directory' : ''"
                        matTooltipPosition="left">
                  <mat-icon>edit</mat-icon> Change Username
                </button>
                <button mat-menu-item (click)="openChangePassword(user)"
                        [disabled]="isLdapUser(user)"
                        [matTooltip]="isLdapUser(user) ? 'Managed by Active Directory' : ''"
                        matTooltipPosition="left">
                  <mat-icon>lock</mat-icon> Change Password
                </button>
                <button mat-menu-item (click)="resetPassword(user)"
                        [disabled]="isLdapUser(user)"
                        [matTooltip]="isLdapUser(user) ? 'Managed by Active Directory' : ''"
                        matTooltipPosition="left">
                  <mat-icon>lock_reset</mat-icon> Reset Password
                </button>
                <button mat-menu-item (click)="openChangeRoles(user)">
                  <mat-icon>manage_accounts</mat-icon> Change Roles
                </button>
                <button mat-menu-item (click)="toggleActive(user)">
                  <mat-icon>{{ user.isActive ? 'block' : 'check_circle' }}</mat-icon>
                  {{ user.isActive ? 'Deactivate' : 'Activate' }}
                </button>
              </mat-menu>
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
          <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
        </table>

        <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons></mat-paginator>
      </div>

      <!-- Edit Username Dialog -->
      <div class="overlay" *ngIf="editingUser && editMode === 'username'" (click)="cancelEdit()">
        <mat-card class="dialog-card" (click)="$event.stopPropagation()">
          <mat-card-header>
            <mat-card-title>Change Username</mat-card-title>
            <mat-card-subtitle>Current: {{ editingUser.username }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>New Username</mat-label>
              <input matInput [(ngModel)]="newUsername">
            </mat-form-field>
          </mat-card-content>
          <mat-card-actions align="end">
            <button mat-button (click)="cancelEdit()">Cancel</button>
            <button mat-raised-button color="primary" (click)="saveUsername()"
                    [disabled]="!newUsername || saving">
              {{ saving ? 'Saving...' : 'Save' }}
            </button>
          </mat-card-actions>
        </mat-card>
      </div>

      <!-- Change Password Dialog -->
      <div class="overlay" *ngIf="editingUser && editMode === 'password'" (click)="cancelEdit()">
        <mat-card class="dialog-card" (click)="$event.stopPropagation()">
          <mat-card-header>
            <mat-card-title>Change Password</mat-card-title>
            <mat-card-subtitle>User: {{ editingUser.username }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>New Password</mat-label>
              <input matInput [(ngModel)]="newPassword" type="password">
              <mat-hint>Minimum 6 characters</mat-hint>
            </mat-form-field>
          </mat-card-content>
          <mat-card-actions align="end">
            <button mat-button (click)="cancelEdit()">Cancel</button>
            <button mat-raised-button color="primary" (click)="savePassword()"
                    [disabled]="!newPassword || newPassword.length < 6 || saving">
              {{ saving ? 'Saving...' : 'Save' }}
            </button>
          </mat-card-actions>
        </mat-card>
      </div>

      <!-- Change Roles Dialog -->
      <div class="overlay" *ngIf="editingUser && editMode === 'roles'" (click)="cancelEdit()">
        <mat-card class="dialog-card" (click)="$event.stopPropagation()">
          <mat-card-header>
            <mat-card-title>Change Roles</mat-card-title>
            <mat-card-subtitle>User: {{ editingUser.username }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Roles</mat-label>
              <mat-select [(ngModel)]="selectedRoleIds" multiple>
                <mat-option *ngFor="let role of roles" [value]="role.id">{{ role.name }}</mat-option>
              </mat-select>
            </mat-form-field>
          </mat-card-content>
          <mat-card-actions align="end">
            <button mat-button (click)="cancelEdit()">Cancel</button>
            <button mat-raised-button color="primary" (click)="saveRoles()"
                    [disabled]="selectedRoleIds.length === 0 || saving">
              {{ saving ? 'Saving...' : 'Save' }}
            </button>
          </mat-card-actions>
        </mat-card>
      </div>

      <!-- Reset Password Result Dialog -->
      <div class="overlay" *ngIf="tempPassword" (click)="tempPassword = null">
        <mat-card class="dialog-card" (click)="$event.stopPropagation()">
          <mat-card-header>
            <mat-card-title>Password Reset</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <p>The temporary password has been set. Please share it securely with the user:</p>
            <div class="temp-password">{{ tempPassword }}</div>
            <p class="hint">The user should change this password on their next login.</p>
          </mat-card-content>
          <mat-card-actions align="end">
            <button mat-raised-button color="primary" (click)="tempPassword = null">Close</button>
          </mat-card-actions>
        </mat-card>
      </div>
    </div>
  `,
  styles: [`
    .header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .form-card { margin-bottom: 24px; }
    .form-row { display: flex; gap: 16px; }
    .form-row mat-form-field { flex: 1; }
    .form-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 8px; }
    .loading { display: flex; justify-content: center; padding: 40px; }
    .table-wrapper { margin-top: 8px; }
    .filter-field { width: 100%; }
    .full-width { width: 100%; }
    table { width: 100%; }
    .active { background-color: var(--status-active) !important; color: white !important; }
    .inactive { background-color: var(--status-inactive) !important; color: white !important; }
    .admin-chip { background-color: var(--status-active) !important; color: white !important; }
    .overlay {
      position: fixed; top: 0; left: 0; right: 0; bottom: 0;
      background: rgba(0, 0, 0, 0.5); display: flex;
      align-items: center; justify-content: center; z-index: 1000;
    }
    .dialog-card { width: 400px; max-width: 90vw; }
    .temp-password {
      background: var(--bg-secondary); padding: 12px 16px; border-radius: 4px;
      font-family: monospace; font-size: 18px; text-align: center;
      margin: 16px 0; user-select: all; letter-spacing: 1px;
    }
    .hint { color: var(--text-secondary); font-size: 13px; }
  `]
})
export class UserManagementComponent implements OnInit {
  loading = false;
  saving = false;
  showCreateForm = false;
  users: SystemUser[] = [];
  roles: Role[] = [];
  dataSource = new MatTableDataSource<SystemUser>();
  displayedColumns = ['username', 'name', 'email', 'roles', 'authSource', 'status', 'actions'];

  createForm!: FormGroup;

  // Edit state
  editingUser: SystemUser | null = null;
  editMode: 'username' | 'password' | 'roles' | null = null;
  newUsername = '';
  newPassword = '';
  selectedRoleIds: string[] = [];
  tempPassword: string | null = null;

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private queryService: QueryService,
    private snackBar: MatSnackBar,
    private fb: FormBuilder,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.createForm = this.fb.group({
      username: ['', [Validators.required, Validators.maxLength(100)]],
      email: ['', [Validators.required, Validators.email, Validators.maxLength(200)]],
      password: ['', [Validators.required, Validators.minLength(6), Validators.maxLength(100)]],
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.maxLength(100)]],
      department: [''],
      roleIds: [[], Validators.required]
    });

    this.loadRoles();
    this.loadUsers();
  }

  loadRoles(): void {
    this.queryService.getRoles().subscribe({
      next: (roles) => this.roles = roles,
      error: () => this.snackBar.open('Failed to load roles', 'Close', { duration: 5000 })
    });
  }

  loadUsers(): void {
    this.loading = true;
    this.queryService.getAllUsers().pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (users) => {
        this.users = users;
        this.dataSource.data = users;
        this.dataSource.paginator = this.paginator;
        this.dataSource.sort = this.sort;
        this.dataSource.sortingDataAccessor = (item: SystemUser, property: string) => {
          switch (property) {
            case 'name': return `${item.firstName} ${item.lastName}`;
            case 'status': return item.isActive ? 'Active' : 'Inactive';
            default: return (item as any)[property];
          }
        };
        this.dataSource.filterPredicate = (data: SystemUser, filter: string) => {
          const searchStr = `${data.username} ${data.firstName} ${data.lastName} ${data.email} ${data.department || ''} ${data.roles.join(' ')}`.toLowerCase();
          return searchStr.includes(filter);
        };
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.snackBar.open('Failed to load users', 'Close', { duration: 5000 });
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  applyFilter(event: Event): void {
    const filterValue = (event.target as HTMLInputElement).value;
    this.dataSource.filter = filterValue.trim().toLowerCase();
  }

  createUser(): void {
    if (this.createForm.invalid) return;
    this.saving = true;
    this.queryService.createUser(this.createForm.value).subscribe({
      next: () => {
        this.snackBar.open('User created successfully', 'Close', { duration: 3000 });
        this.saving = false;
        this.showCreateForm = false;
        this.createForm.reset();
        this.loadUsers();
      },
      error: (err) => {
        this.snackBar.open(err.error?.message || 'Failed to create user', 'Close', { duration: 5000 });
        this.saving = false;
      }
    });
  }

  cancelCreate(): void {
    this.showCreateForm = false;
    this.createForm.reset();
  }

  openEditUsername(user: SystemUser): void {
    this.editingUser = user;
    this.editMode = 'username';
    this.newUsername = user.username;
  }

  openChangePassword(user: SystemUser): void {
    this.editingUser = user;
    this.editMode = 'password';
    this.newPassword = '';
  }

  openChangeRoles(user: SystemUser): void {
    this.editingUser = user;
    this.editMode = 'roles';
    this.selectedRoleIds = this.roles
      .filter(r => user.roles.includes(r.name))
      .map(r => r.id);
  }

  cancelEdit(): void {
    this.editingUser = null;
    this.editMode = null;
  }

  saveUsername(): void {
    if (!this.editingUser || !this.newUsername) return;
    this.saving = true;
    this.queryService.changeUsername(this.editingUser.id, { newUsername: this.newUsername }).subscribe({
      next: () => {
        this.snackBar.open('Username changed successfully', 'Close', { duration: 3000 });
        this.saving = false;
        this.cancelEdit();
        this.loadUsers();
      },
      error: (err) => {
        this.snackBar.open(err.error?.message || 'Failed to change username', 'Close', { duration: 5000 });
        this.saving = false;
      }
    });
  }

  savePassword(): void {
    if (!this.editingUser || !this.newPassword) return;
    this.saving = true;
    this.queryService.changePassword(this.editingUser.id, { newPassword: this.newPassword }).subscribe({
      next: () => {
        this.snackBar.open('Password changed successfully', 'Close', { duration: 3000 });
        this.saving = false;
        this.cancelEdit();
      },
      error: (err) => {
        this.snackBar.open(err.error?.message || 'Failed to change password', 'Close', { duration: 5000 });
        this.saving = false;
      }
    });
  }

  resetPassword(user: SystemUser): void {
    this.saving = true;
    this.queryService.resetPassword(user.id).subscribe({
      next: (result) => {
        this.tempPassword = result.temporaryPassword;
        this.saving = false;
      },
      error: (err) => {
        this.snackBar.open(err.error?.message || 'Failed to reset password', 'Close', { duration: 5000 });
        this.saving = false;
      }
    });
  }

  saveRoles(): void {
    if (!this.editingUser || this.selectedRoleIds.length === 0) return;
    this.saving = true;
    this.queryService.changeUserRoles(this.editingUser.id, { roleIds: this.selectedRoleIds }).subscribe({
      next: () => {
        this.snackBar.open('Roles updated successfully', 'Close', { duration: 3000 });
        this.saving = false;
        this.cancelEdit();
        this.loadUsers();
      },
      error: (err) => {
        this.snackBar.open(err.error?.message || 'Failed to update roles', 'Close', { duration: 5000 });
        this.saving = false;
      }
    });
  }

  isLdapUser(user: SystemUser): boolean {
    return (user.authSource || '').toLowerCase() === 'ldap';
  }

  toggleActive(user: SystemUser): void {
    const newState = !user.isActive;
    this.queryService.toggleUserActive(user.id, { isActive: newState }).subscribe({
      next: () => {
        this.snackBar.open(
          `User ${newState ? 'activated' : 'deactivated'} successfully`, 'Close', { duration: 3000 }
        );
        this.loadUsers();
      },
      error: (err) => {
        this.snackBar.open(err.error?.message || 'Failed to update user status', 'Close', { duration: 5000 });
      }
    });
  }
}
