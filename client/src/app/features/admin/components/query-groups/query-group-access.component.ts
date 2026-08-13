import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, FormGroup } from '@angular/forms';
import { ToastService } from '@core/services/toast.service';
import { grantsQueryAccess, roleLabel } from '@core/models/roles';
import { forkJoin, throwError } from 'rxjs';
import { timeout, catchError } from 'rxjs/operators';
import { QueryService } from '@core/services/query.service';
import { QueryGroup, Role, SystemUser, UserGroup } from '@core/models/dynamic-query.model';

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

            <mat-tab [label]="'admin.access.userGroupsTab' | transloco">
              <form [formGroup]="userGroupsForm" (ngSubmit)="onSaveUserGroups()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.access.assignedUserGroups' | transloco }}</mat-label>
                  <mat-select formControlName="userGroupIds" multiple>
                    <mat-option *ngFor="let group of userGroups" [value]="group.id">
                      {{ group.name }}
                      <span class="member-count">({{ 'admin.access.memberCount' | transloco: { count: group.memberCount } }})</span>
                    </mat-option>
                  </mat-select>
                  <mat-hint *ngIf="userGroups.length === 0">{{ 'admin.access.noUserGroups' | transloco }}</mat-hint>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/query-groups">{{ 'common.cancel' | transloco }}</button>
                  <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                    {{ saving ? ('common.saving' | transloco) : ('admin.access.saveUserGroups' | transloco) }}
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
    .member-count { color: var(--text-secondary); }
  `]
})
export class QueryGroupAccessComponent implements OnInit {
  rolesForm!: FormGroup;
  userGroupsForm!: FormGroup;
  usersForm!: FormGroup;
  group?: QueryGroup;
  roles: Role[] = [];

  /** "AccessManager" -> "Access Manager" for display. */
  roleLabel = roleLabel;
  userGroups: UserGroup[] = [];
  users: SystemUser[] = [];
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
    this.userGroupsForm = this.fb.group({ userGroupIds: [[]] });
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
      userGroups: this.queryService.getAllUserGroups(),
      users: this.queryService.getAllUsers()
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
        this.userGroups = result.userGroups;
        this.users = result.users;
        this.rolesForm.patchValue({
          roleIds: result.group.assignedRoles.map(r => r.roleId)
        });
        this.userGroupsForm.patchValue({
          userGroupIds: result.group.assignedUserGroups.map(g => g.userGroupId)
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

  onSaveUserGroups(): void {
    this.saving = true;
    this.queryService.assignGroupUserGroups(this.groupId, { userGroupIds: this.userGroupsForm.value.userGroupIds }).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.access.userGroupsAssigned');
        this.router.navigate(['/admin/query-groups']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.access.userGroupsFailed');
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
