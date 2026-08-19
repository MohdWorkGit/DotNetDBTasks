import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, FormGroup } from '@angular/forms';
import { ToastService } from '@core/services/toast.service';
import { roleLabel } from '@core/models/roles';
import { forkJoin, throwError } from 'rxjs';
import { timeout, catchError } from 'rxjs/operators';
import { QueryService } from '@core/services/query.service';
import { ScheduledTaskService } from '@core/services/scheduled-task.service';
import { Role, SystemUser, UserGroup } from '@core/models/dynamic-query.model';
import { ScheduledTaskAccess } from '@core/models/scheduled-task.model';

/**
 * Manages who may see a scheduled task's status and run history, and which of those may
 * also download its export files — the same roles / user groups / users shape the query
 * and query-group access pages use.
 *
 * Each tab saves independently and replaces only its own principal type's grants, so two
 * admins editing different tabs cannot wipe each other's work. The downloaders picker on
 * each tab offers only that tab's chosen viewers: downloading is the narrower right, and
 * the API drops any id that was not also sent as a viewer.
 */
@Component({
  standalone: false,
  selector: 'app-scheduled-task-access',
  template: `
    <div class="container">
      <h2>{{ 'admin.tasks.accessTitle' | transloco }}</h2>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <div *ngIf="!loading && errorMessage" class="error-block">
        <p class="error-text">{{ errorMessage }}</p>
        <button mat-raised-button color="primary" (click)="loadData()">
          <mat-icon>refresh</mat-icon> {{ 'common.retry' | transloco }}
        </button>
      </div>

      <mat-card *ngIf="!loading && access">
        <mat-card-header>
          <mat-card-title>{{ access.taskName }}</mat-card-title>
          <mat-card-subtitle>{{ 'admin.tasks.accessSubtitle' | transloco }}</mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          <mat-tab-group>
            <mat-tab [label]="'admin.access.rolesTab' | transloco">
              <form [formGroup]="rolesForm" (ngSubmit)="onSaveRoles()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.tasks.viewerRoles' | transloco }}</mat-label>
                  <mat-select formControlName="roleIds" multiple (selectionChange)="pruneDownloads('role')">
                    <mat-option *ngFor="let role of roles" [value]="role.id">
                      {{ roleLabel(role.name) }}<span *ngIf="role.description"> - {{ role.description }}</span>
                    </mat-option>
                  </mat-select>
                  <mat-hint>{{ 'admin.tasks.viewersHint' | transloco }}</mat-hint>
                </mat-form-field>

                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.tasks.downloadRoles' | transloco }}</mat-label>
                  <mat-select formControlName="downloadRoleIds" multiple>
                    <mat-option *ngFor="let role of selectedRoles" [value]="role.id">
                      {{ roleLabel(role.name) }}
                    </mat-option>
                  </mat-select>
                  <mat-hint>{{ 'admin.tasks.downloadersHint' | transloco }}</mat-hint>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/scheduled-tasks">
                    {{ 'common.cancel' | transloco }}
                  </button>
                  <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                    {{ (saving ? 'common.saving' : 'admin.access.saveRoles') | transloco }}
                  </button>
                </div>
              </form>
            </mat-tab>

            <mat-tab [label]="'admin.access.userGroupsTab' | transloco">
              <form [formGroup]="userGroupsForm" (ngSubmit)="onSaveUserGroups()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.tasks.viewerUserGroups' | transloco }}</mat-label>
                  <mat-select formControlName="userGroupIds" multiple (selectionChange)="pruneDownloads('group')">
                    <mat-option *ngFor="let group of userGroups" [value]="group.id">
                      {{ group.name }}
                      <span class="member-count">({{ 'admin.access.memberCount' | transloco: { count: group.memberCount } }})</span>
                    </mat-option>
                  </mat-select>
                  <mat-hint *ngIf="userGroups.length === 0">{{ 'admin.access.noUserGroups' | transloco }}</mat-hint>
                  <mat-hint *ngIf="userGroups.length > 0">{{ 'admin.tasks.viewersHint' | transloco }}</mat-hint>
                </mat-form-field>

                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.tasks.downloadUserGroups' | transloco }}</mat-label>
                  <mat-select formControlName="downloadUserGroupIds" multiple>
                    <mat-option *ngFor="let group of selectedUserGroups" [value]="group.id">
                      {{ group.name }}
                    </mat-option>
                  </mat-select>
                  <mat-hint>{{ 'admin.tasks.downloadersHint' | transloco }}</mat-hint>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/scheduled-tasks">
                    {{ 'common.cancel' | transloco }}
                  </button>
                  <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                    {{ (saving ? 'common.saving' : 'admin.access.saveUserGroups') | transloco }}
                  </button>
                </div>
              </form>
            </mat-tab>

            <mat-tab [label]="'admin.access.usersTab' | transloco">
              <form [formGroup]="usersForm" (ngSubmit)="onSaveUsers()" class="tab-content">
                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.tasks.viewerUsers' | transloco }}</mat-label>
                  <mat-select formControlName="userIds" multiple (selectionChange)="pruneDownloads('user')">
                    <mat-option *ngFor="let user of users" [value]="user.id">
                      {{ user.username }} ({{ user.firstName }} {{ user.lastName }})
                    </mat-option>
                  </mat-select>
                  <mat-hint>{{ 'admin.tasks.viewersHint' | transloco }}</mat-hint>
                </mat-form-field>

                <mat-form-field class="full-width" appearance="outline">
                  <mat-label>{{ 'admin.tasks.downloadUsers' | transloco }}</mat-label>
                  <mat-select formControlName="downloadUserIds" multiple>
                    <mat-option *ngFor="let user of selectedUsers" [value]="user.id">
                      {{ user.username }}
                    </mat-option>
                  </mat-select>
                  <mat-hint>{{ 'admin.tasks.downloadersHint' | transloco }}</mat-hint>
                </mat-form-field>

                <div class="actions">
                  <button mat-button type="button" routerLink="/admin/scheduled-tasks">
                    {{ 'common.cancel' | transloco }}
                  </button>
                  <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                    {{ (saving ? 'common.saving' : 'admin.access.saveUsers') | transloco }}
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
export class ScheduledTaskAccessComponent implements OnInit {
  rolesForm!: FormGroup;
  userGroupsForm!: FormGroup;
  usersForm!: FormGroup;

  access?: ScheduledTaskAccess;
  roles: Role[] = [];
  userGroups: UserGroup[] = [];
  users: SystemUser[] = [];
  saving = false;
  loading = true;
  errorMessage = '';

  /** "AccessManager" -> "Access Manager" for display. */
  roleLabel = roleLabel;

  private taskId = '';

  constructor(
    private fb: FormBuilder,
    private tasks: ScheduledTaskService,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private router: Router,
    private toast: ToastService,
    private cdr: ChangeDetectorRef
  ) {
    this.rolesForm = this.fb.group({ roleIds: [[]], downloadRoleIds: [[]] });
    this.userGroupsForm = this.fb.group({ userGroupIds: [[]], downloadUserGroupIds: [[]] });
    this.usersForm = this.fb.group({ userIds: [[]], downloadUserIds: [[]] });
  }

  ngOnInit(): void {
    this.taskId = this.route.snapshot.params['id'];
    this.loadData();
  }

  /** Only granted viewers can be offered the extra download permission. */
  get selectedRoles(): Role[] {
    const ids: string[] = this.rolesForm.get('roleIds')?.value || [];
    return this.roles.filter(r => ids.includes(r.id));
  }

  get selectedUserGroups(): UserGroup[] {
    const ids: string[] = this.userGroupsForm.get('userGroupIds')?.value || [];
    return this.userGroups.filter(g => ids.includes(g.id));
  }

  get selectedUsers(): SystemUser[] {
    const ids: string[] = this.usersForm.get('userIds')?.value || [];
    return this.users.filter(u => ids.includes(u.id));
  }

  /** Removing a viewer also revokes their download grant. */
  pruneDownloads(kind: 'role' | 'group' | 'user'): void {
    const form =
      kind === 'role' ? this.rolesForm :
      kind === 'group' ? this.userGroupsForm :
      this.usersForm;
    const viewerKey =
      kind === 'role' ? 'roleIds' :
      kind === 'group' ? 'userGroupIds' :
      'userIds';
    const downloadKey =
      kind === 'role' ? 'downloadRoleIds' :
      kind === 'group' ? 'downloadUserGroupIds' :
      'downloadUserIds';

    const viewerIds: string[] = form.get(viewerKey)?.value || [];
    const download: string[] = form.get(downloadKey)?.value || [];
    const pruned = download.filter(id => viewerIds.includes(id));
    if (pruned.length !== download.length) {
      form.get(downloadKey)?.setValue(pruned);
    }
  }

  loadData(): void {
    this.loading = true;
    this.errorMessage = '';

    forkJoin({
      access: this.tasks.getAccess(this.taskId),
      // Every role is offered — unlike query access, seeing a task's status is not gated
      // on being able to run queries, so nothing here would be a no-op grant.
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
        this.access = result.access;
        this.roles = result.roles;
        this.userGroups = result.userGroups;
        this.users = result.users;

        this.rolesForm.patchValue({
          roleIds: result.access.roles.map(r => r.roleId),
          downloadRoleIds: result.access.roles.filter(r => r.canDownloadFiles).map(r => r.roleId)
        });
        this.userGroupsForm.patchValue({
          userGroupIds: result.access.userGroups.map(g => g.userGroupId),
          downloadUserGroupIds: result.access.userGroups.filter(g => g.canDownloadFiles).map(g => g.userGroupId)
        });
        this.usersForm.patchValue({
          userIds: result.access.users.map(u => u.userId),
          downloadUserIds: result.access.users.filter(u => u.canDownloadFiles).map(u => u.userId)
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
    const value = this.rolesForm.value;
    this.tasks.assignAccessRoles(this.taskId, {
      roleIds: value.roleIds || [],
      downloadRoleIds: (value.downloadRoleIds || [])
        .filter((id: string) => (value.roleIds || []).includes(id))
    }).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.access.rolesAssigned');
        this.router.navigate(['/admin/scheduled-tasks']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.access.rolesFailed');
      }
    });
  }

  onSaveUserGroups(): void {
    this.saving = true;
    const value = this.userGroupsForm.value;
    this.tasks.assignAccessUserGroups(this.taskId, {
      userGroupIds: value.userGroupIds || [],
      downloadUserGroupIds: (value.downloadUserGroupIds || [])
        .filter((id: string) => (value.userGroupIds || []).includes(id))
    }).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.access.userGroupsAssigned');
        this.router.navigate(['/admin/scheduled-tasks']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.access.userGroupsFailed');
      }
    });
  }

  onSaveUsers(): void {
    this.saving = true;
    const value = this.usersForm.value;
    this.tasks.assignAccessUsers(this.taskId, {
      userIds: value.userIds || [],
      downloadUserIds: (value.downloadUserIds || [])
        .filter((id: string) => (value.userIds || []).includes(id))
    }).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.access.usersAssigned');
        this.router.navigate(['/admin/scheduled-tasks']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.access.usersFailed');
      }
    });
  }
}
