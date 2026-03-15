import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { QueryService } from '@core/services/query.service';
import { DatabaseUser, SystemUser } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-database-users',
  template: `
    <div class="container">
      <div class="header">
        <h2>Database Users</h2>
        <button mat-raised-button color="primary" (click)="showForm = true; resetForm()">
          <mat-icon>add</mat-icon> Add Database User
        </button>
      </div>

      <!-- Form Dialog -->
      <mat-card *ngIf="showForm" class="form-card">
        <mat-card-header>
          <mat-card-title>{{ editingId ? 'Edit' : 'Create' }} Database User</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <div class="form-grid">
              <mat-form-field appearance="outline">
                <mat-label>Name</mat-label>
                <input matInput formControlName="name" placeholder="e.g. Production Read-Only">
                <mat-hint>Friendly name for this connection</mat-hint>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Description</mat-label>
                <input matInput formControlName="description">
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Host</mat-label>
                <input matInput formControlName="host" placeholder="e.g. db.example.com">
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Port</mat-label>
                <input matInput type="number" formControlName="port">
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Service Name</mat-label>
                <input matInput formControlName="serviceName" placeholder="e.g. XEPDB1">
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>DB Username</mat-label>
                <input matInput formControlName="dbUsername">
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ editingId ? 'New Password (leave blank to keep)' : 'Password' }}</mat-label>
                <input matInput type="password" formControlName="password">
              </mat-form-field>

              <mat-slide-toggle *ngIf="editingId" formControlName="isActive">Active</mat-slide-toggle>
            </div>

            <div class="actions">
              <button mat-button type="button" (click)="showForm = false">Cancel</button>
              <button mat-raised-button color="primary" type="submit"
                      [disabled]="form.invalid || saving">
                {{ saving ? 'Saving...' : (editingId ? 'Update' : 'Create') }}
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>

      <!-- List -->
      <mat-card *ngFor="let du of dbUsers" class="db-user-card">
        <mat-card-header>
          <mat-card-title>
            {{ du.name }}
            <mat-icon [class.active]="du.isActive" [class.inactive]="!du.isActive"
                      [matTooltip]="du.isActive ? 'Active' : 'Inactive'">
              {{ du.isActive ? 'check_circle' : 'cancel' }}
            </mat-icon>
          </mat-card-title>
          <mat-card-subtitle>{{ du.dbUsername }}@{{ du.host }}:{{ du.port }}/{{ du.serviceName }}</mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          <p *ngIf="du.description" class="description">{{ du.description }}</p>

          <div class="access-section">
            <strong>Allowed Users:</strong>
            <mat-chip-set>
              <mat-chip *ngFor="let u of du.assignedUsers">{{ u.username }}</mat-chip>
              <mat-chip *ngIf="!du.assignedUsers?.length" class="none-chip">None assigned</mat-chip>
            </mat-chip-set>
          </div>
        </mat-card-content>

        <mat-card-actions>
          <button mat-button (click)="editDbUser(du)">
            <mat-icon>edit</mat-icon> Edit
          </button>
          <button mat-button (click)="openAccessDialog(du)">
            <mat-icon>people</mat-icon> Manage Access
          </button>
          <button mat-button (click)="testConnection(du)">
            <mat-icon>wifi_tethering</mat-icon> Test Connection
          </button>
          <button mat-button color="warn" (click)="deleteDbUser(du)">
            <mat-icon>delete</mat-icon> Delete
          </button>
        </mat-card-actions>
      </mat-card>

      <p *ngIf="!dbUsers.length && !loading" class="empty">No database users configured yet.</p>

      <!-- Access Management Dialog -->
      <mat-card *ngIf="accessDialogDbUser" class="form-card">
        <mat-card-header>
          <mat-card-title>Manage Access: {{ accessDialogDbUser.name }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <p class="hint">Select which application users can execute queries using this database user.</p>
          <div class="user-checkboxes">
            <mat-checkbox *ngFor="let user of allUsers"
                          [checked]="selectedUserIds.has(user.id)"
                          (change)="toggleUserAccess(user.id, $event.checked)">
              {{ user.username }} ({{ user.firstName }} {{ user.lastName }})
            </mat-checkbox>
          </div>
          <div class="actions">
            <button mat-button (click)="accessDialogDbUser = null">Cancel</button>
            <button mat-raised-button color="primary" (click)="saveAccess()" [disabled]="savingAccess">
              {{ savingAccess ? 'Saving...' : 'Save Access' }}
            </button>
          </div>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
    .form-card { margin-bottom: 24px; }
    .form-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
      gap: 16px;
    }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 16px; }
    .db-user-card { margin-bottom: 16px; }
    .description { color: var(--text-secondary); margin: 0 0 12px; }
    .access-section { margin-top: 8px; }
    .active { color: var(--status-success); }
    .inactive { color: var(--status-error); }
    .none-chip { opacity: 0.5; }
    .empty { text-align: center; color: var(--text-secondary); padding: 40px 0; }
    .hint { font-size: 13px; color: var(--text-secondary); margin-bottom: 12px; }
    .user-checkboxes { display: flex; flex-direction: column; gap: 8px; max-height: 300px; overflow-y: auto; }
  `]
})
export class DatabaseUsersComponent implements OnInit {
  dbUsers: DatabaseUser[] = [];
  allUsers: SystemUser[] = [];
  showForm = false;
  form!: FormGroup;
  editingId: string | null = null;
  saving = false;
  loading = true;

  accessDialogDbUser: DatabaseUser | null = null;
  selectedUserIds = new Set<string>();
  savingAccess = false;

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.resetForm();
    this.loadDbUsers();
    this.queryService.getAllUsers().subscribe({
      next: (users) => { this.allUsers = users; },
      error: () => {}
    });
  }

  resetForm(): void {
    this.editingId = null;
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: ['', Validators.maxLength(1000)],
      host: ['', Validators.required],
      port: [1521, [Validators.required, Validators.min(1)]],
      serviceName: ['', Validators.required],
      dbUsername: ['', Validators.required],
      password: ['', Validators.required],
      isActive: [true]
    });
  }

  loadDbUsers(): void {
    this.loading = true;
    this.queryService.getAllDatabaseUsers().subscribe({
      next: (users) => {
        this.dbUsers = users;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.snackBar.open('Failed to load database users', 'Close', { duration: 5000 });
      }
    });
  }

  editDbUser(du: DatabaseUser): void {
    this.editingId = du.id;
    this.showForm = true;
    this.form.patchValue({
      name: du.name,
      description: du.description,
      host: du.host,
      port: du.port,
      serviceName: du.serviceName,
      dbUsername: du.dbUsername,
      isActive: du.isActive,
      password: ''
    });
    // Password not required when editing
    this.form.get('password')?.clearValidators();
    this.form.get('password')?.updateValueAndValidity();
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.saving = true;
    const val = this.form.value;

    if (this.editingId) {
      this.queryService.updateDatabaseUser(this.editingId, {
        id: this.editingId,
        ...val,
        password: val.password || undefined
      }).subscribe({
        next: () => {
          this.saving = false;
          this.showForm = false;
          this.snackBar.open('Database user updated', 'Close', { duration: 3000 });
          this.loadDbUsers();
        },
        error: (err) => {
          this.saving = false;
          this.snackBar.open(err.error?.message || 'Update failed', 'Close', { duration: 5000 });
        }
      });
    } else {
      this.queryService.createDatabaseUser(val).subscribe({
        next: () => {
          this.saving = false;
          this.showForm = false;
          this.snackBar.open('Database user created', 'Close', { duration: 3000 });
          this.loadDbUsers();
        },
        error: (err) => {
          this.saving = false;
          this.snackBar.open(err.error?.message || 'Creation failed', 'Close', { duration: 5000 });
        }
      });
    }
  }

  deleteDbUser(du: DatabaseUser): void {
    if (!confirm(`Delete database user "${du.name}"? Queries using it will fall back to the default connection.`)) return;
    this.queryService.deleteDatabaseUser(du.id).subscribe({
      next: () => {
        this.snackBar.open('Database user deleted', 'Close', { duration: 3000 });
        this.loadDbUsers();
      },
      error: (err) => {
        this.snackBar.open(err.error?.message || 'Delete failed', 'Close', { duration: 5000 });
      }
    });
  }

  testConnection(du: DatabaseUser): void {
    this.snackBar.open('Testing connection...', '', { duration: 10000 });
    this.queryService.testDatabaseConnection(du.id).subscribe({
      next: (result) => {
        if (result.success) {
          this.snackBar.open('Connection successful!', 'Close', { duration: 3000 });
        } else {
          this.snackBar.open(`Connection failed: ${result.errorMessage}`, 'Close', { duration: 8000 });
        }
      },
      error: (err) => {
        this.snackBar.open(err.error?.message || 'Test failed', 'Close', { duration: 5000 });
      }
    });
  }

  openAccessDialog(du: DatabaseUser): void {
    this.accessDialogDbUser = du;
    this.selectedUserIds = new Set(du.assignedUsers?.map(u => u.userId) || []);
  }

  toggleUserAccess(userId: string, checked: boolean): void {
    if (checked) {
      this.selectedUserIds.add(userId);
    } else {
      this.selectedUserIds.delete(userId);
    }
  }

  saveAccess(): void {
    if (!this.accessDialogDbUser) return;
    this.savingAccess = true;
    this.queryService.assignDatabaseUserAccess(this.accessDialogDbUser.id, {
      userIds: Array.from(this.selectedUserIds)
    }).subscribe({
      next: () => {
        this.savingAccess = false;
        this.accessDialogDbUser = null;
        this.snackBar.open('Access updated', 'Close', { duration: 3000 });
        this.loadDbUsers();
      },
      error: (err) => {
        this.savingAccess = false;
        this.snackBar.open(err.error?.message || 'Failed to update access', 'Close', { duration: 5000 });
      }
    });
  }
}
