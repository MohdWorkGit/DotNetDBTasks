import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '@core/services/toast.service';
import { timeout, catchError } from 'rxjs/operators';
import { forkJoin, Observable, of, throwError } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { QueryService } from '@core/services/query.service';
import { SystemUser } from '@core/models/dynamic-query.model';
import { AuthService } from '@core/services/auth.service';

@Component({
  standalone: false,
  selector: 'app-user-group-form',
  template: `
    <div class="container">
      <h2>{{ (isEdit ? 'admin.userGroups.editTitle' : 'admin.userGroups.createTitle') | transloco }}</h2>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <mat-card *ngIf="!loading">
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.userGroups.name' | transloco }}</mat-label>
              <input matInput formControlName="name" maxlength="200">
              <mat-error *ngIf="form.get('name')?.hasError('required')">
                {{ 'admin.userGroups.nameRequired' | transloco }}
              </mat-error>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.userGroups.description' | transloco }}</mat-label>
              <textarea matInput formControlName="description" rows="2" maxlength="1000"></textarea>
            </mat-form-field>

            <h3 class="section-title">{{ 'admin.userGroups.members' | transloco }}</h3>
            <p class="section-hint">{{ 'admin.userGroups.membersHint' | transloco }}</p>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.userGroups.filterUsers' | transloco }}</mat-label>
              <input matInput [(ngModel)]="userFilter" [ngModelOptions]="{ standalone: true }"
                     [attr.placeholder]="'admin.userGroups.filterUsersPlaceholder' | transloco">
              <mat-icon matSuffix>search</mat-icon>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.userGroups.members' | transloco }}</mat-label>
              <mat-select formControlName="memberUserIds" multiple>
                <!-- The list is filtered, so the default trigger would hide selections that
                     no longer match the filter. A count always tells the truth. -->
                <mat-select-trigger>
                  {{ 'admin.userGroups.selectedCount' | transloco: { count: selectedCount() } }}
                </mat-select-trigger>
                <mat-option *ngFor="let user of filteredUsers()" [value]="user.id"
                            [disabled]="cannotJoin(user)">
                  {{ user.username }} ({{ user.firstName }} {{ user.lastName }})
                  <span *ngIf="!user.isActive"> — {{ 'admin.userGroups.inactive' | transloco }}</span>
                </mat-option>
              </mat-select>
              <mat-hint *ngIf="!authService.isAdmin()">{{ 'admin.userGroups.noSelfHint' | transloco }}</mat-hint>
            </mat-form-field>

            <div class="actions">
              <button mat-button type="button" routerLink="/admin/user-groups">{{ 'common.cancel' | transloco }}</button>
              <button mat-raised-button color="primary" type="submit"
                      [disabled]="form.invalid || saving">
                {{ saving ? ('common.saving' | transloco) : ((isEdit ? 'common.update' : 'common.create') | transloco) }}
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 16px; }
    .full-width { width: 100%; }
    .section-title { margin: 8px 0 4px; }
    .section-hint { color: var(--text-secondary); margin: 0 0 16px; }
  `]
})
export class UserGroupFormComponent implements OnInit {
  form!: FormGroup;
  isEdit = false;
  groupId?: string;
  saving = false;
  loading = true;
  users: SystemUser[] = [];
  userFilter = '';
  /** Membership as loaded, so an existing member is never mistaken for someone joining. */
  private initialMemberIds = new Set<string>();

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private router: Router,
    private toast: ToastService,
    public authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: ['', [Validators.maxLength(1000)]],
      memberUserIds: [[] as string[]]
    });

    this.groupId = this.route.snapshot.params['id'];
    this.isEdit = !!this.groupId;
    this.load();
  }

  private load(): void {
    this.loading = true;

    forkJoin({
      users: this.queryService.getAllUsers(),
      group: this.groupId ? this.queryService.getUserGroupById(this.groupId) : of(null)
    }).pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: ({ users, group }) => {
        this.users = users;
        if (group) {
          this.initialMemberIds = new Set(group.members.map(m => m.userId));
          this.form.patchValue({
            name: group.name,
            description: group.description,
            memberUserIds: group.members.map(m => m.userId)
          });
        }
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'admin.userGroups.loadOneFailed');
        this.cdr.detectChanges();
      }
    });
  }

  filteredUsers(): SystemUser[] {
    const f = this.userFilter.trim().toLowerCase();
    if (!f) return this.users;
    return this.users.filter(u =>
      `${u.username} ${u.firstName} ${u.lastName} ${u.email || ''}`.toLowerCase().includes(f));
  }

  /**
   * True for the caller's own account when they are not an Admin and are not already in the
   * group — the one selection the API refuses (see AdminAccountGuard.EnsureNotJoiningGroup).
   * Someone an administrator already put in the group stays selectable, so saving the form
   * cannot silently drop them.
   */
  cannotJoin(user: SystemUser): boolean {
    return !this.authService.isAdmin()
      && user.username === this.authService.getUsername()
      && !this.initialMemberIds.has(user.id);
  }

  selectedCount(): number {
    return (this.form.get('memberUserIds')?.value as string[] | null)?.length ?? 0;
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.saving = true;
    const { name, description, memberUserIds } = this.form.value;

    // Membership is a separate endpoint (and a separate audit entry), so an edit is two
    // calls: rename first, then set the members it now holds.
    const request$: Observable<unknown> = this.isEdit
      ? this.queryService.updateUserGroup(this.groupId!, { id: this.groupId!, name, description }).pipe(
          switchMap(() => this.queryService.setUserGroupMembers(this.groupId!, { userIds: memberUserIds })))
      : this.queryService.createUserGroup({ name, description, memberUserIds });

    request$.pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success(this.isEdit ? 'admin.userGroups.updated' : 'admin.userGroups.created');
        this.router.navigate(['/admin/user-groups']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'common.operationFailed');
      }
    });
  }
}
