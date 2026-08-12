import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, FormGroup } from '@angular/forms';
import { ToastService } from '@core/services/toast.service';
import { grantsQueryAccess, roleLabel } from '@core/models/roles';
import { forkJoin, throwError } from 'rxjs';
import { timeout, catchError } from 'rxjs/operators';
import { QueryService } from '@core/services/query.service';
import { ImportedLdapUser, QueryGroup, Role } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-query-group-access',
  template: `
    <div class="container">
      <h2>{{ 'admin.access.groupTitle' | transloco }}</h2>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <div *ngIf="!loading && errorMessage" class="error-block">
        <p class="error-text">{{ errorMessage }}</p>
        <button mat-raised-button color="primary" (click)="loadData()">
          <mat-icon>refresh</mat-icon> Retry
        </button>
      </div>

      <mat-card *ngIf="!loading && group">
        <mat-card-header>
          <mat-card-title>{{ group.name }}</mat-card-title>
          <mat-card-subtitle>
            Access granted here also grants access to every query inside this group.
          </mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          <mat-tab-group>
            <mat-tab label="Roles">
              <form [formGroup]="rolesForm" (ngSubmit)="onSaveRoles()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.access.assignedRoles' | transloco }}</mat-label>
                  <mat-select formControlName="roleIds" multiple>
                    <mat-option *ngFor="let role of roles" [value]="role.id">
                      {{ roleLabel(role.name) }} <span *ngIf="role.description">- {{ role.description }}</span>
                    </mat-option>
                  </mat-select>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/query-groups">{{ 'common.cancel' | transloco }}</button>
                  <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                    {{ saving ? 'Saving...' : 'Save Roles' }}
                  </button>
                </div>
              </form>
            </mat-tab>

            <mat-tab label="Departments">
              <form [formGroup]="departmentsForm" (ngSubmit)="onSaveDepartments()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.access.assignedDepartments' | transloco }}</mat-label>
                  <mat-select formControlName="departments" multiple>
                    <mat-option *ngFor="let dept of departments" [value]="dept">
                      {{ dept }}
                    </mat-option>
                  </mat-select>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/query-groups">{{ 'common.cancel' | transloco }}</button>
                  <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                    {{ saving ? 'Saving...' : 'Save Departments' }}
                  </button>
                </div>
              </form>
            </mat-tab>

            <mat-tab label="Users">
              <form [formGroup]="usersForm" (ngSubmit)="onSaveUsers()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.access.assignedUsers' | transloco }}</mat-label>
                  <mat-select formControlName="userIds" multiple>
                    <mat-option *ngFor="let user of users" [value]="user.id">
                      {{ user.username }} ({{ user.firstName }} {{ user.lastName }})
                    </mat-option>
                  </mat-select>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/query-groups">{{ 'common.cancel' | transloco }}</button>
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
    .full-width { width: 100%; }
  `]
})
export class QueryGroupAccessComponent implements OnInit {
  rolesForm!: FormGroup;
  departmentsForm!: FormGroup;
  usersForm!: FormGroup;
  group?: QueryGroup;
  roles: Role[] = [];

  /** "AccessManager" -> "Access Manager" for display. */
  roleLabel = roleLabel;
  departments: string[] = [];
  users: ImportedLdapUser[] = [];
  saving = false;
  loading = true;
  errorMessage = '';

  private groupId = '';

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private router: Router,
    private toast: ToastService,
    private cdr: ChangeDetectorRef
  ) {
    this.rolesForm = this.fb.group({ roleIds: [[]] });
    this.departmentsForm = this.fb.group({ departments: [[]] });
    this.usersForm = this.fb.group({ userIds: [[]] });
  }

  ngOnInit(): void {
    this.groupId = this.route.snapshot.params['id'];
    this.loadData();
  }

  loadData(): void {
    this.loading = true;
    this.errorMessage = '';

    forkJoin({
      group: this.queryService.getQueryGroupById(this.groupId),
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
        this.group = result.group;
        // Auditor and AccessManager never run queries, so the API ignores them when
        // resolving who may open a group. Offering them here would only let someone save
        // an assignment that silently does nothing.
        this.roles = result.roles.filter(r => grantsQueryAccess(r.name));
        this.departments = result.departments as string[];
        this.users = result.users as ImportedLdapUser[];
        this.rolesForm.patchValue({
          roleIds: result.group.assignedRoles.map(r => r.roleId)
        });
        this.departmentsForm.patchValue({
          departments: result.group.assignedDepartments.map(d => d.department)
        });
        this.usersForm.patchValue({
          userIds: result.group.assignedUsers.map(u => u.userId)
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
    this.queryService.assignGroupRoles(this.groupId, { roleIds: this.rolesForm.value.roleIds }).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.access.rolesAssigned');
        this.router.navigate(['/admin/query-groups']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.access.rolesFailed');
      }
    });
  }

  onSaveDepartments(): void {
    this.saving = true;
    this.queryService.assignGroupDepartments(this.groupId, { departments: this.departmentsForm.value.departments }).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.access.departmentsAssigned');
        this.router.navigate(['/admin/query-groups']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.access.departmentsFailed');
      }
    });
  }

  onSaveUsers(): void {
    this.saving = true;
    this.queryService.assignGroupUsers(this.groupId, { userIds: this.usersForm.value.userIds }).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.access.usersAssigned');
        this.router.navigate(['/admin/query-groups']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.access.usersFailed');
      }
    });
  }
}
