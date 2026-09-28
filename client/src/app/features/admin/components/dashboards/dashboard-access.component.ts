import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, FormGroup } from '@angular/forms';
import { forkJoin, throwError } from 'rxjs';
import { timeout, catchError } from 'rxjs/operators';
import { ToastService, extractApiError } from '@core/services/toast.service';
import { grantsQueryAccess, roleLabel } from '@core/models/roles';
import { QueryService } from '@core/services/query.service';
import { DashboardService } from '@core/services/dashboard.service';
import { Role, SystemUser, UserGroup } from '@core/models/dynamic-query.model';
import { Dashboard } from '@core/models/dashboard.model';

/**
 * Grants access to a dashboard. Mirrors ReportAccessComponent: one request for all three lists.
 *
 * <p>Reaching a dashboard is only the outer gate. Every tile's query is still checked on its own,
 * on every refresh, so granting a dashboard here cannot show someone data from a query they were
 * not already allowed to run — that tile simply says so.</p>
 */
@Component({
  standalone: false,
  selector: 'app-dashboard-access',
  template: `
    <div class="container">
      <h2>{{ 'admin.dashboards.accessTitle' | transloco }}</h2>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <div *ngIf="!loading && errorMessage" class="error-block">
        <p class="error-text">{{ errorMessage }}</p>
        <button mat-raised-button color="primary" (click)="loadData()">
          <mat-icon>refresh</mat-icon> {{ 'common.retry' | transloco }}
        </button>
      </div>

      <mat-card *ngIf="!loading && dashboard">
        <mat-card-header>
          <mat-card-title dir="auto">{{ dashboard.name }}</mat-card-title>
          <mat-card-subtitle>{{ 'admin.dashboards.accessSubtitle' | transloco }}</mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="save()">
            <mat-tab-group>
              <mat-tab [label]="'admin.access.rolesTab' | transloco">
                <div class="tab-content">
                  <mat-form-field class="full-width" appearance="outline">
                    <mat-label>{{ 'admin.access.assignedRoles' | transloco }}</mat-label>
                    <mat-select formControlName="roleIds" multiple>
                      <mat-option *ngFor="let role of roles" [value]="role.id">
                        {{ roleLabel(role.name) }}
                      </mat-option>
                    </mat-select>
                  </mat-form-field>
                </div>
              </mat-tab>

              <mat-tab [label]="'admin.access.userGroupsTab' | transloco">
                <div class="tab-content">
                  <mat-form-field class="full-width" appearance="outline">
                    <mat-label>{{ 'admin.access.assignedUserGroups' | transloco }}</mat-label>
                    <mat-select formControlName="userGroupIds" multiple>
                      <mat-option *ngFor="let group of userGroups" [value]="group.id">
                        {{ group.name }}
                        <span class="muted">
                          ({{ 'admin.access.memberCount' | transloco: { count: group.memberCount } }})
                        </span>
                      </mat-option>
                    </mat-select>
                    <mat-hint *ngIf="userGroups.length === 0">
                      {{ 'admin.access.noUserGroups' | transloco }}
                    </mat-hint>
                  </mat-form-field>
                </div>
              </mat-tab>

              <mat-tab [label]="'admin.access.usersTab' | transloco">
                <div class="tab-content">
                  <mat-form-field class="full-width" appearance="outline">
                    <mat-label>{{ 'admin.access.assignedUsers' | transloco }}</mat-label>
                    <mat-select formControlName="userIds" multiple>
                      <mat-option *ngFor="let user of users" [value]="user.id">
                        {{ user.username }} ({{ user.firstName }} {{ user.lastName }})
                      </mat-option>
                    </mat-select>
                  </mat-form-field>
                </div>
              </mat-tab>
            </mat-tab-group>

            <div class="actions">
              <button mat-button type="button" routerLink="/admin/dashboards">
                {{ 'common.cancel' | transloco }}
              </button>
              <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                {{ (saving ? 'common.saving' : 'common.save') | transloco }}
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-block-start: 24px; }
    .tab-content { padding-block-start: 24px; }
    .full-width { inline-size: 100%; }
    .muted { color: var(--text-secondary); }
  `]
})
export class DashboardAccessComponent implements OnInit {
  private fb = inject(FormBuilder);
  private dashboards = inject(DashboardService);
  private queryService = inject(QueryService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private toast = inject(ToastService);
  private cdr = inject(ChangeDetectorRef);

  form: FormGroup = this.fb.group({ roleIds: [[]], userGroupIds: [[]], userIds: [[]] });
  dashboard?: Dashboard;
  roles: Role[] = [];
  userGroups: UserGroup[] = [];
  users: SystemUser[] = [];

  loading = true;
  saving = false;
  errorMessage = '';

  /** "AccessManager" -> "Access Manager" for display. */
  roleLabel = roleLabel;

  private dashboardId = '';

  ngOnInit(): void {
    this.dashboardId = this.route.snapshot.params['id'];
    this.loadData();
  }

  loadData(): void {
    this.loading = true;
    this.errorMessage = '';

    forkJoin({
      dashboard: this.dashboards.getById(this.dashboardId),
      access: this.dashboards.getAccess(this.dashboardId),
      roles: this.queryService.getRoles(),
      userGroups: this.queryService.getAllUserGroups(),
      users: this.queryService.getAllUsers()
    }).pipe(
      timeout(30000),
      catchError(err => throwError(() => err))
    ).subscribe({
      next: result => {
        this.dashboard = result.dashboard;
        // Auditor and AccessManager never run anything, so granting them a dashboard whose
        // tiles they could not fill would only let someone save a no-op.
        this.roles = result.roles.filter(r => grantsQueryAccess(r.name));
        this.userGroups = result.userGroups;
        this.users = result.users;
        this.form.patchValue({
          roleIds: result.access.roleIds ?? [],
          userGroupIds: result.access.userGroupIds ?? [],
          userIds: result.access.userIds ?? []
        });
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: err => {
        this.loading = false;
        this.errorMessage = extractApiError(err, 'admin.dashboards.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  save(): void {
    this.saving = true;
    this.dashboards.setAccess(this.dashboardId, this.form.value).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.dashboards.accessSaved');
        this.router.navigate(['/admin/dashboards']);
      },
      error: err => {
        this.saving = false;
        this.toast.error(err, 'admin.dashboards.accessFailed');
        this.cdr.detectChanges();
      }
    });
  }
}
