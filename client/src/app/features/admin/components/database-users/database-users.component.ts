import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { QueryService } from '@core/services/query.service';
import { DatabaseUser, DatabaseServerType, Role } from '@core/models/dynamic-query.model';
import { roleLabel, grantsQueryAccess } from '@core/models/roles';
import { TranslocoService } from '@jsverse/transloco';

@Component({
  standalone: false,
  selector: 'app-database-users',
  template: `
    <div class="container">
      <div class="header">
        <h2>{{ 'admin.dbUsers.title' | transloco }}</h2>
        <button mat-raised-button color="primary" (click)="showForm = true; resetForm()">
          <mat-icon>add</mat-icon> {{ 'admin.dbUsers.add' | transloco }}
        </button>
      </div>

      <!-- Form Dialog -->
      <mat-card *ngIf="showForm" class="form-card">
        <mat-card-header>
          <mat-card-title>{{ (editingId ? 'admin.dbUsers.editTitle' : (isCopy ? 'admin.dbUsers.copyTitle' : 'admin.dbUsers.createTitle')) | transloco }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <div class="form-grid">
              <mat-form-field appearance="outline">
                <mat-label>{{ 'admin.dbUsers.name' | transloco }}</mat-label>
                <input matInput formControlName="name" [attr.placeholder]="'admin.dbUsers.namePlaceholder' | transloco">
                <mat-hint>{{ 'admin.dbUsers.nameHint' | transloco }}</mat-hint>
                <mat-error *ngIf="form.get('name')?.hasError('required')">Name is required</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ 'admin.dbUsers.description' | transloco }}</mat-label>
                <input matInput formControlName="description">
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ 'admin.dbUsers.serverType' | transloco }}</mat-label>
                <mat-select formControlName="serverType" (selectionChange)="onServerTypeChange($event.value)">
                  <mat-option *ngFor="let st of serverTypes" [value]="st.value">{{ st.label }}</mat-option>
                </mat-select>
                <mat-error *ngIf="form.get('serverType')?.hasError('required')">Database type is required</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ 'admin.dbUsers.host' | transloco }}</mat-label>
                <input matInput formControlName="host" [attr.placeholder]="'admin.dbUsers.hostPlaceholder' | transloco">
                <mat-error *ngIf="form.get('host')?.hasError('required')">Host is required</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ 'admin.dbUsers.port' | transloco }}</mat-label>
                <input matInput type="number" formControlName="port">
                <mat-error *ngIf="form.get('port')?.hasError('required')">Port is required</mat-error>
              </mat-form-field>

              <mat-form-field *ngIf="isOracle" appearance="outline">
                <mat-label>{{ 'admin.dbUsers.serviceName' | transloco }}</mat-label>
                <input matInput formControlName="serviceName" [attr.placeholder]="'admin.dbUsers.serviceNamePlaceholder' | transloco">
              </mat-form-field>

              <mat-form-field *ngIf="!isOracle" appearance="outline">
                <mat-label>{{ 'admin.dbUsers.databaseName' | transloco }}</mat-label>
                <input matInput formControlName="databaseName" [attr.placeholder]="'admin.dbUsers.databaseNamePlaceholder' | transloco">
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ 'admin.dbUsers.dbUsername' | transloco }}</mat-label>
                <input matInput formControlName="dbUsername">
                <mat-error *ngIf="form.get('dbUsername')?.hasError('required')">DB username is required</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>{{ (editingId ? 'admin.dbUsers.newPassword' : 'admin.users.password') | transloco }}</mat-label>
                <input matInput type="password" formControlName="password">
                <mat-error *ngIf="form.get('password')?.hasError('required')">Password is required</mat-error>
              </mat-form-field>

              <mat-slide-toggle *ngIf="editingId" formControlName="isActive">{{ 'common.active' | transloco }}</mat-slide-toggle>
            </div>

            <div class="actions">
              <button mat-button type="button" (click)="showForm = false">{{ 'common.cancel' | transloco }}</button>
              <!-- Kept enabled when invalid so clicking it reveals the errors rather
                   than leaving the user with a dead button and no explanation. -->
              <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                {{ saving ? ('common.saving' | transloco) : ((editingId ? 'common.update' : 'common.create') | transloco) }}
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
                      [matTooltip]="(du.isActive ? 'common.active' : 'common.inactive') | transloco"
                      [attr.aria-label]="(du.isActive ? 'common.active' : 'common.inactive') | transloco"
                      role="img">
              {{ du.isActive ? 'check_circle' : 'cancel' }}
            </mat-icon>
          </mat-card-title>
          <mat-card-subtitle>
            <span class="db-type-badge">{{ getServerTypeLabel(du.serverType) }}</span>
            {{ du.dbUsername }}@{{ du.host }}:{{ du.port }}/{{ du.serverType === 0 ? du.serviceName : du.databaseName }}
          </mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          <p *ngIf="du.description" class="description">{{ du.description }}</p>

          <div class="access-section">
            <strong>{{ 'admin.dbUsers.allowedRoles' | transloco }}</strong>
            <mat-chip-set>
              <mat-chip *ngFor="let r of du.assignedRoles">{{ roleLabel(r.roleName) }}</mat-chip>
              <mat-chip *ngIf="!du.assignedRoles?.length" class="none-chip">{{ 'admin.dbUsers.noneAssigned' | transloco }}</mat-chip>
            </mat-chip-set>
          </div>
        </mat-card-content>

        <mat-card-actions>
          <button mat-button (click)="editDbUser(du)">
            <mat-icon>edit</mat-icon> {{ 'common.edit' | transloco }}
          </button>
          <button mat-button (click)="copyDbUser(du)">
            <mat-icon>content_copy</mat-icon> {{ 'common.copy' | transloco }}
          </button>
          <button mat-button (click)="openAccessDialog(du)">
            <mat-icon>people</mat-icon> {{ 'admin.common.manageAccess' | transloco }}
          </button>
          <button mat-button (click)="testConnection(du)">
            <mat-icon>wifi_tethering</mat-icon> {{ 'admin.dbUsers.testConnection' | transloco }}
          </button>
          <button mat-button color="warn" (click)="deleteDbUser(du)">
            <mat-icon>delete</mat-icon> {{ 'common.delete' | transloco }}
          </button>
        </mat-card-actions>
      </mat-card>

      <p *ngIf="!dbUsers.length && !loading" class="empty">{{ 'admin.dbUsers.none' | transloco }}</p>

      <!-- Access Management Dialog -->
      <mat-card *ngIf="accessDialogDbUser" class="form-card">
        <mat-card-header>
          <mat-card-title>{{ 'admin.dbUsers.manageAccessFor' | transloco: { name: accessDialogDbUser.name } }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <p class="hint">{{ 'admin.dbUsers.accessHint' | transloco }}</p>
          <div class="user-checkboxes">
            <mat-checkbox *ngFor="let role of allRoles"
                          [checked]="selectedRoleIds.has(role.id)"
                          (change)="toggleRoleAccess(role.id, $event.checked)">
              {{ roleLabel(role.name) }}<span *ngIf="role.description"> &mdash; {{ role.description }}</span>
            </mat-checkbox>
          </div>
          <div class="actions">
            <button mat-button (click)="accessDialogDbUser = null">{{ 'common.cancel' | transloco }}</button>
            <button mat-raised-button color="primary" (click)="saveAccess()" [disabled]="savingAccess">
              {{ savingAccess ? 'Saving...' : 'Save Access' }}
            </button>
          </div>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
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
    .db-type-badge {
      display: inline-block;
      background: var(--accent-primary);
      color: #fff;
      font-size: 11px;
      font-weight: 500;
      padding: 2px 8px;
      border-radius: 12px;
      margin-inline-end: 6px;
      vertical-align: middle;
    }
  `]
})
export class DatabaseUsersComponent implements OnInit {
  dbUsers: DatabaseUser[] = [];
  allRoles: Role[] = [];

  /** "AccessManager" -> "Access Manager" for display. */
  roleLabel = roleLabel;
  showForm = false;
  form!: FormGroup;
  editingId: string | null = null;
  isCopy = false;
  saving = false;
  loading = true;

  accessDialogDbUser: DatabaseUser | null = null;
  selectedRoleIds = new Set<string>();
  savingAccess = false;

  serverTypes = [
    { value: DatabaseServerType.Oracle, label: 'Oracle', defaultPort: 1521 },
    { value: DatabaseServerType.SqlServer, label: 'SQL Server', defaultPort: 1433 },
    { value: DatabaseServerType.PostgreSql, label: 'PostgreSQL', defaultPort: 5432 },
    { value: DatabaseServerType.MySql, label: 'MySQL', defaultPort: 3306 }
  ];

  get isOracle(): boolean {
    return this.form?.get('serverType')?.value === DatabaseServerType.Oracle;
  }

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private toast: ToastService,
    private confirmService: ConfirmService,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.resetForm();
    this.loadDbUsers();
    this.queryService.getRoles().subscribe({
      // Auditor and AccessManager never execute queries, so a database-user grant to
      // either can never be used — leave them out rather than offer a no-op.
      next: (roles) => { this.allRoles = roles.filter(r => grantsQueryAccess(r.name)); },
      error: () => {}
    });
  }

  resetForm(): void {
    this.editingId = null;
    this.isCopy = false;
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: ['', Validators.maxLength(1000)],
      serverType: [DatabaseServerType.Oracle, Validators.required],
      host: ['', Validators.required],
      port: [1521, [Validators.required, Validators.min(1)]],
      serviceName: [''],
      databaseName: [''],
      dbUsername: ['', Validators.required],
      password: ['', Validators.required],
      isActive: [true]
    });
  }

  onServerTypeChange(value: DatabaseServerType): void {
    const defaultPort = this.serverTypes.find(s => s.value === value)?.defaultPort ?? 1521;
    this.form.patchValue({ port: defaultPort });
  }

  getServerTypeLabel(value: DatabaseServerType): string {
    return this.serverTypes.find(s => s.value === value)?.label ?? 'Unknown';
  }

  loadDbUsers(): void {
    this.loading = true;
    this.queryService.getAllDatabaseUsers().subscribe({
      next: (users) => {
        this.dbUsers = users;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'admin.dbUsers.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  editDbUser(du: DatabaseUser): void {
    this.editingId = du.id;
    this.isCopy = false;
    this.showForm = true;
    this.form.patchValue({
      name: du.name,
      description: du.description,
      serverType: du.serverType,
      host: du.host,
      port: du.port,
      serviceName: du.serviceName,
      databaseName: du.databaseName,
      dbUsername: du.dbUsername,
      isActive: du.isActive,
      password: ''
    });
    // Password not required when editing
    this.form.get('password')?.clearValidators();
    this.form.get('password')?.updateValueAndValidity();
  }

  copyDbUser(du: DatabaseUser): void {
    this.resetForm();
    this.isCopy = true;
    this.showForm = true;
    this.form.patchValue({
      name: `Copy of ${du.name}`,
      description: du.description,
      serverType: du.serverType,
      host: du.host,
      port: du.port,
      serviceName: du.serviceName,
      databaseName: du.databaseName,
      dbUsername: du.dbUsername,
      password: '',
      isActive: true
    });
  }

  onSubmit(): void {
    if (this.form.invalid) {
      // Reveal the <mat-error>s: untouched controls render no error until marked.
      this.form.markAllAsTouched();
      this.toast.error('common.correctFields', 'common.correctFields');
      this.cdr.detectChanges();
      return;
    }
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
          this.toast.success('admin.dbUsers.updated');
          this.loadDbUsers();
        },
        error: (err) => {
          this.saving = false;
          this.toast.error(err, 'common.updateFailed');
        }
      });
    } else {
      this.queryService.createDatabaseUser(val).subscribe({
        next: () => {
          this.saving = false;
          this.showForm = false;
          this.toast.success('admin.dbUsers.created');
          this.loadDbUsers();
        },
        error: (err) => {
          this.saving = false;
          this.toast.error(err, 'common.createFailed');
        }
      });
    }
  }

  deleteDbUser(du: DatabaseUser): void {
    this.confirmService.askThen({
      titleKey: 'admin.dbUsers.deleteTitle',
      messageKey: 'admin.dbUsers.deleteMessage',
      params: { name: du.name },
      confirmText: this.transloco.translate('common.delete'),
      destructive: true
    }, () => {
      this.queryService.deleteDatabaseUser(du.id).subscribe({
        next: () => {
          this.toast.success('admin.dbUsers.deleted');
          this.loadDbUsers();
        },
        error: (err) => {
          this.toast.error(err, 'common.deleteFailed');
        }
      });
    });
  }

  testConnection(du: DatabaseUser): void {
    this.toast.info('admin.dbUsers.testing', 10000);
    this.queryService.testDatabaseConnection(du.id).subscribe({
      next: (result) => {
        if (result.success) {
          this.toast.success('admin.dbUsers.testOk');
        } else {
          this.toast.error(
            `Connection failed: ${result.errorMessage}`,
            'Connection failed',
            8000
          );
        }
      },
      error: (err) => {
        this.toast.error(err, 'admin.dbUsers.testFailed');
      }
    });
  }

  openAccessDialog(du: DatabaseUser): void {
    this.accessDialogDbUser = du;
    this.selectedRoleIds = new Set(du.assignedRoles?.map(r => r.roleId) || []);
  }

  toggleRoleAccess(roleId: string, checked: boolean): void {
    if (checked) {
      this.selectedRoleIds.add(roleId);
    } else {
      this.selectedRoleIds.delete(roleId);
    }
  }

  saveAccess(): void {
    if (!this.accessDialogDbUser) return;
    this.savingAccess = true;
    this.queryService.assignDatabaseUserAccess(this.accessDialogDbUser.id, {
      roleIds: Array.from(this.selectedRoleIds)
    }).subscribe({
      next: () => {
        this.savingAccess = false;
        this.accessDialogDbUser = null;
        this.toast.success('admin.dbUsers.accessUpdated');
        this.loadDbUsers();
      },
      error: (err) => {
        this.savingAccess = false;
        this.toast.error(err, 'admin.dbUsers.accessUpdateFailed');
      }
    });
  }
}
