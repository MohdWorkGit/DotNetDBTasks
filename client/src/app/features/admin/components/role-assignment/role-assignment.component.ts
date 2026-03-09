import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, FormGroup } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, throwError } from 'rxjs';
import { timeout, catchError } from 'rxjs/operators';
import { QueryService } from '@core/services/query.service';
import { DynamicQuery, ImportedLdapUser, Role } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-role-assignment',
  template: `
    <div class="container">
      <h2>Manage Query Access</h2>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <div *ngIf="!loading && errorMessage" class="error-block">
        <p class="error-text">{{ errorMessage }}</p>
        <button mat-raised-button color="primary" (click)="loadData()">
          <mat-icon>refresh</mat-icon> Retry
        </button>
      </div>

      <mat-card *ngIf="!loading && query">
        <mat-card-header>
          <mat-card-title>{{ query.name }}</mat-card-title>
          <mat-card-subtitle>{{ query.description }}</mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          <mat-tab-group>
            <!-- Roles Tab -->
            <mat-tab label="Roles">
              <form [formGroup]="rolesForm" (ngSubmit)="onSaveRoles()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>Assigned Roles</mat-label>
                  <mat-select formControlName="roleIds" multiple>
                    <mat-option *ngFor="let role of roles" [value]="role.id">
                      {{ role.name }} <span *ngIf="role.description">- {{ role.description }}</span>
                    </mat-option>
                  </mat-select>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/queries">Cancel</button>
                  <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                    {{ saving ? 'Saving...' : 'Save Roles' }}
                  </button>
                </div>
              </form>
            </mat-tab>

            <!-- Departments Tab -->
            <mat-tab label="Departments">
              <form [formGroup]="departmentsForm" (ngSubmit)="onSaveDepartments()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>Assigned Departments</mat-label>
                  <mat-select formControlName="departments" multiple>
                    <mat-option *ngFor="let dept of departments" [value]="dept">
                      {{ dept }}
                    </mat-option>
                  </mat-select>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/queries">Cancel</button>
                  <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                    {{ saving ? 'Saving...' : 'Save Departments' }}
                  </button>
                </div>
              </form>
            </mat-tab>

            <!-- Users Tab -->
            <mat-tab label="Users">
              <form [formGroup]="usersForm" (ngSubmit)="onSaveUsers()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>Assigned Users</mat-label>
                  <mat-select formControlName="userIds" multiple>
                    <mat-option *ngFor="let user of users" [value]="user.id">
                      {{ user.username }} ({{ user.firstName }} {{ user.lastName }})
                    </mat-option>
                  </mat-select>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/queries">Cancel</button>
                  <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                    {{ saving ? 'Saving...' : 'Save Users' }}
                  </button>
                </div>
              </form>
            </mat-tab>
          </mat-tab-group>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 24px; }
    .tab-content { padding-top: 24px; }
    .loading { display: flex; justify-content: center; padding: 40px; }
    .error-block { text-align: center; padding: 24px; }
    .error-text { color: var(--status-error); margin-bottom: 16px; }
  `]
})
export class RoleAssignmentComponent implements OnInit {
  rolesForm!: FormGroup;
  departmentsForm!: FormGroup;
  usersForm!: FormGroup;
  query?: DynamicQuery;
  roles: Role[] = [];
  departments: string[] = [];
  users: ImportedLdapUser[] = [];
  saving = false;
  loading = true;
  errorMessage = '';

  private queryId = '';

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {
    this.rolesForm = this.fb.group({ roleIds: [[]] });
    this.departmentsForm = this.fb.group({ departments: [[]] });
    this.usersForm = this.fb.group({ userIds: [[]] });
  }

  ngOnInit(): void {
    this.queryId = this.route.snapshot.params['id'];
    this.loadData();
  }

  loadData(): void {
    this.loading = true;
    this.errorMessage = '';

    forkJoin({
      query: this.queryService.getQueryById(this.queryId),
      roles: this.queryService.getRoles(),
      departments: this.queryService.getLdapDepartments().pipe(catchError(() => [])),
      users: this.queryService.getImportedLdapUsers().pipe(catchError(() => []))
    }).pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out. Please try again.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (result) => {
        this.query = result.query;
        this.roles = result.roles;
        this.departments = result.departments as string[];
        this.users = result.users as ImportedLdapUser[];
        this.rolesForm.patchValue({
          roleIds: result.query.assignedRoles.map(r => r.roleId)
        });
        this.departmentsForm.patchValue({
          departments: result.query.assignedDepartments.map(d => d.department)
        });
        this.usersForm.patchValue({
          userIds: result.query.assignedUsers.map(u => u.userId)
        });
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || 'Failed to load data. Please try again.';
        this.cdr.detectChanges();
      }
    });
  }

  onSaveRoles(): void {
    this.saving = true;
    const queryId = this.route.snapshot.params['id'];
    this.queryService.assignRoles(queryId, { roleIds: this.rolesForm.value.roleIds }).subscribe({
      next: () => {
        this.saving = false;
        this.snackBar.open('Roles assigned successfully', 'Close', { duration: 3000 });
      },
      error: () => {
        this.saving = false;
        this.snackBar.open('Failed to assign roles', 'Close', { duration: 5000 });
      }
    });
  }

  onSaveDepartments(): void {
    this.saving = true;
    const queryId = this.route.snapshot.params['id'];
    this.queryService.assignDepartments(queryId, { departments: this.departmentsForm.value.departments }).subscribe({
      next: () => {
        this.saving = false;
        this.snackBar.open('Departments assigned successfully', 'Close', { duration: 3000 });
      },
      error: () => {
        this.saving = false;
        this.snackBar.open('Failed to assign departments', 'Close', { duration: 5000 });
      }
    });
  }

  onSaveUsers(): void {
    this.saving = true;
    const queryId = this.route.snapshot.params['id'];
    this.queryService.assignUsers(queryId, { userIds: this.usersForm.value.userIds }).subscribe({
      next: () => {
        this.saving = false;
        this.snackBar.open('Users assigned successfully', 'Close', { duration: 3000 });
      },
      error: () => {
        this.saving = false;
        this.snackBar.open('Failed to assign users', 'Close', { duration: 5000 });
      }
    });
  }
}
