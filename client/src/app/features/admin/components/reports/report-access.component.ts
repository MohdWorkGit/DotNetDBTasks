import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, FormGroup } from '@angular/forms';
import { forkJoin, throwError } from 'rxjs';
import { timeout, catchError } from 'rxjs/operators';
import { ToastService, extractApiError } from '@core/services/toast.service';
import { grantsQueryAccess, roleLabel } from '@core/models/roles';
import { QueryService } from '@core/services/query.service';
import { ReportService } from '@core/services/report.service';
import { Role, SystemUser, UserGroup } from '@core/models/dynamic-query.model';
import { Report } from '@core/models/report.model';

/**
 * Grants access to a report.
 *
 * <p>Saved in one request rather than one per tab, because the API models a report's access as a
 * single value — roles, user groups and users together — unlike the query pages, whose endpoints
 * are separate. Saving one tab therefore has to send the other two as they stand.</p>
 *
 * <p>Reaching a report is only the outer gate. Every dataset's underlying query is still checked
 * on its own when the report runs, so granting a report here cannot hand someone a query they
 * were not already allowed to run.</p>
 */
@Component({
  standalone: false,
  selector: 'app-report-access',
  template: `
    <div class="container">
      <h2>{{ 'admin.reports.accessTitle' | transloco }}</h2>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <div *ngIf="!loading && errorMessage" class="error-block">
        <p class="error-text">{{ errorMessage }}</p>
        <button mat-raised-button color="primary" (click)="loadData()">
          <mat-icon>refresh</mat-icon> {{ 'common.retry' | transloco }}
        </button>
      </div>

      <mat-card *ngIf="!loading && report">
        <mat-card-header>
          <mat-card-title dir="auto">{{ report.name }}</mat-card-title>
          <mat-card-subtitle>{{ 'admin.reports.accessSubtitle' | transloco }}</mat-card-subtitle>
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
              <button mat-button type="button" routerLink="/admin/reports">
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
export class ReportAccessComponent implements OnInit {
  private fb = inject(FormBuilder);
  private reports = inject(ReportService);
  private queryService = inject(QueryService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private toast = inject(ToastService);
  private cdr = inject(ChangeDetectorRef);

  form: FormGroup = this.fb.group({ roleIds: [[]], userGroupIds: [[]], userIds: [[]] });
  report?: Report;
  roles: Role[] = [];
  userGroups: UserGroup[] = [];
  users: SystemUser[] = [];

  loading = true;
  saving = false;
  errorMessage = '';

  /** "AccessManager" -> "Access Manager" for display. */
  roleLabel = roleLabel;

  private reportId = '';

  ngOnInit(): void {
    this.reportId = this.route.snapshot.params['id'];
    this.loadData();
  }

  loadData(): void {
    this.loading = true;
    this.errorMessage = '';

    forkJoin({
      report: this.reports.getById(this.reportId),
      access: this.reports.getAccess(this.reportId),
      roles: this.queryService.getRoles(),
      userGroups: this.queryService.getAllUserGroups(),
      users: this.queryService.getAllUsers()
    }).pipe(
      timeout(30000),
      catchError(err => throwError(() => err))
    ).subscribe({
      next: result => {
        this.report = result.report;
        // Auditor and AccessManager never run anything, so the API ignores them when it works
        // out who may open a report. Offering them would only let someone save a no-op.
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
        this.errorMessage = extractApiError(err, 'admin.reports.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  save(): void {
    this.saving = true;
    this.reports.setAccess(this.reportId, this.form.value).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.reports.accessSaved');
        this.router.navigate(['/admin/reports']);
      },
      error: err => {
        this.saving = false;
        this.toast.error(err, 'admin.reports.accessFailed');
        this.cdr.detectChanges();
      }
    });
  }
}
