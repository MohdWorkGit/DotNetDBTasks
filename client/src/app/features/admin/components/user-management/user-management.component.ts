import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { ToastService } from '@core/services/toast.service';
import { roleLabel } from '@core/models/roles';
import { ConfirmService } from '@core/services/confirm.service';
import {
  UserEditDialogComponent,
  UserEditDialogData
} from '@shared/components/user-edit-dialog.component';
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
                <mat-label>Email (optional)</mat-label>
                <input matInput formControlName="email" type="email">
                <mat-hint>Leave blank if the user has no address</mat-hint>
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
                <mat-option *ngFor="let role of roles" [value]="role.id">{{ roleLabel(role.name) }}</mat-option>
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
            <td mat-cell *matCellDef="let user">
              <span *ngIf="user.email; else noEmail">{{ user.email }}</span>
              <ng-template #noEmail><span class="no-value">&mdash;</span></ng-template>
            </td>
          </ng-container>

          <ng-container matColumnDef="roles">
            <th mat-header-cell *matHeaderCellDef>Roles</th>
            <td mat-cell *matCellDef="let user">
              <mat-chip-listbox>
                <mat-chip *ngFor="let role of user.roles" [class.admin-chip]="role === 'Admin'">
                  {{ roleLabel(role) }}
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
              <button mat-icon-button [matMenuTriggerFor]="actionMenu" matTooltip="Actions" aria-label="Actions">
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

          <tr class="mat-row no-data-row" *matNoDataRow>
            <td class="mat-cell no-data-cell" [attr.colspan]="displayedColumns.length">
              No users match the current filter.
            </td>
          </tr>
        </table>

        <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons></mat-paginator>
      </div>

    </div>
  `,
  styles: [`
    .form-card { margin-bottom: 24px; }
    .form-row { display: flex; gap: 16px; }
    .form-row mat-form-field { flex: 1; }
    .form-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 8px; }
    .table-wrapper { margin-top: 8px; }
    .filter-field { width: 100%; }
    .full-width { width: 100%; }
    table { width: 100%; }
    /* Chip fills use the --chip-* tokens, not --status-*: the status colors are tuned
       as foreground colors and gave white label text ~1.6:1 in dark mode. These pairs
       measure 11:1 light / 8.7:1 dark. Matches ad-users.component.ts. */
    .active { background-color: var(--chip-active) !important; color: var(--chip-text) !important; }
    .inactive { background-color: var(--chip-inactive) !important; color: var(--chip-text) !important; }
    .admin-chip { background-color: var(--chip-accent) !important; color: var(--chip-text) !important; }
    .no-value { color: var(--text-secondary); }
  `]
})
export class UserManagementComponent implements OnInit {
  loading = false;
  saving = false;
  showCreateForm = false;
  users: SystemUser[] = [];
  roles: Role[] = [];

  /** Turns the wire name into a display label ("AccessManager" -> "Access Manager"). */
  roleLabel = roleLabel;
  dataSource = new MatTableDataSource<SystemUser>();
  displayedColumns = ['username', 'name', 'email', 'roles', 'authSource', 'status', 'actions'];

  createForm!: FormGroup;


  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(
    private queryService: QueryService,
    private toast: ToastService,
    private confirmService: ConfirmService,
    private dialog: MatDialog,
    private fb: FormBuilder,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.createForm = this.fb.group({
      username: ['', [Validators.required, Validators.maxLength(100)]],
      // Optional: Validators.email already passes on an empty control, so dropping
      // Validators.required is all that is needed to allow a blank address.
      email: ['', [Validators.email, Validators.maxLength(200)]],
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
      error: (err) => this.toast.error(err, 'Failed to load roles')
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
        // Bind paginator/sort after the *ngIf table has rendered
        setTimeout(() => {
          this.dataSource.paginator = this.paginator;
          this.dataSource.sort = this.sort;
        });
      },
      error: (err) => {
        this.toast.error(err, 'Failed to load users');
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
        this.toast.success('User created successfully');
        this.saving = false;
        this.showCreateForm = false;
        this.createForm.reset();
        this.loadUsers();
      },
      error: (err) => {
        this.toast.error(err, 'Failed to create user');
        this.saving = false;
      }
    });
  }

  cancelCreate(): void {
    this.showCreateForm = false;
    this.createForm.reset();
  }

  openEditUsername(user: SystemUser): void {
    this.openUserDialog({
      mode: 'username',
      username: user.username,
      save: (value) => this.queryService.changeUsername(user.id, { newUsername: value as string })
    }, 'Username changed successfully', true);
  }

  openChangePassword(user: SystemUser): void {
    this.openUserDialog({
      mode: 'password',
      username: user.username,
      save: (value) => this.queryService.changePassword(user.id, { newPassword: value as string })
    }, 'Password changed successfully', false);
  }

  openChangeRoles(user: SystemUser): void {
    this.openUserDialog({
      mode: 'roles',
      username: user.username,
      roles: this.roles,
      selectedRoleIds: this.roles.filter(r => user.roles.includes(r.name)).map(r => r.id),
      save: (value) => this.queryService.changeUserRoles(user.id, { roleIds: value as string[] })
    }, 'Roles updated successfully', true);
  }

  /**
   * Opens the shared edit dialog and, on a successful save, toasts and optionally
   * reloads. MatDialog handles the focus trap, Escape, and focus restore that the
   * previous hand-rolled overlays lacked.
   */
  private openUserDialog(data: UserEditDialogData, successMessage: string, reload: boolean): void {
    this.dialog.open(UserEditDialogComponent, { data, width: '420px', ariaModal: true })
      .afterClosed()
      .subscribe(saved => {
        if (saved) {
          this.toast.success(successMessage);
          if (reload) this.loadUsers();
        }
        this.cdr.detectChanges();
      });
  }

  resetPassword(user: SystemUser): void {
    this.confirmService.askThen({
      title: 'Reset password?',
      message: `${user.username}'s current password will stop working immediately and be replaced `
        + 'by a temporary one that you must pass on to them. This cannot be undone.',
      confirmText: 'Reset password',
      destructive: true
    }, () => {
      this.saving = true;
      this.cdr.detectChanges();
      this.queryService.resetPassword(user.id).subscribe({
        next: (result) => {
          this.saving = false;
          this.dialog.open(UserEditDialogComponent, {
            data: { mode: 'result', username: user.username, tempPassword: result.temporaryPassword },
            width: '420px',
            ariaModal: true
          });
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.toast.error(err, 'Failed to reset password');
          this.saving = false;
          this.cdr.detectChanges();
        }
      });
    });
  }

  isLdapUser(user: SystemUser): boolean {
    return (user.authSource || '').toLowerCase() === 'ldap';
  }

  toggleActive(user: SystemUser): void {
    const newState = !user.isActive;
    // Reactivating is harmless; deactivating locks the user out immediately.
    if (!newState) {
      this.confirmService.askThen({
        title: 'Deactivate user?',
        message: `${user.username} will be signed out and blocked from logging in until an admin `
          + 'reactivates the account.',
        confirmText: 'Deactivate',
        destructive: true
      }, () => this.setActive(user, newState));
      return;
    }
    this.setActive(user, newState);
  }

  private setActive(user: SystemUser, newState: boolean): void {
    this.queryService.toggleUserActive(user.id, { isActive: newState }).subscribe({
      next: () => {
        this.toast.success(`User ${newState ? 'activated' : 'deactivated'} successfully`);
        this.loadUsers();
      },
      error: (err) => {
        this.toast.error(err, 'Failed to update user status');
      }
    });
  }
}
