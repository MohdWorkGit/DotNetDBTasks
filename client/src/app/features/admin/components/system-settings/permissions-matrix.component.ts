import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { QueryService } from '@core/services/query.service';
import { PermissionMatrix, RolePermissions } from '@core/models/dynamic-query.model';
import {
  PERMISSION_GROUPS,
  permissionHintKey,
  permissionLabelKey
} from '@core/models/permissions';
import { TranslocoService } from '@jsverse/transloco';
import { forkJoin, of } from 'rxjs';

/**
 * The permission matrix: what every role may do, in one grid.
 *
 * <p>Roles are columns and capabilities are rows, because the question this page answers is
 * "who can do X?" — asked across roles, not about one of them. Grouping the rows by area keeps
 * a 23-row grid readable.</p>
 *
 * <p>Nothing saves as you tick. A permission change is a security decision, so the edits are
 * held until Save and sent as one round per changed role, with the untouched roles left alone.</p>
 */
@Component({
  standalone: false,
  selector: 'app-permissions-matrix',
  template: `
    <div class="container">
      <div class="header">
        <h2>{{ 'admin.permissions.title' | transloco }}</h2>
      </div>

    <div *ngIf="loading" class="loading">
      <mat-spinner diameter="40"></mat-spinner>
    </div>

    <ng-container *ngIf="!loading && matrix">
      <div class="toolbar">
        <p class="hint">{{ 'admin.permissions.intro' | transloco }}</p>
        <button mat-stroked-button (click)="startCreate()" [disabled]="saving">
          <mat-icon>add</mat-icon> {{ 'admin.permissions.addRole' | transloco }}
        </button>
      </div>

      <div class="table-wrapper">
        <table class="matrix">
          <thead>
            <tr>
              <th class="capability">{{ 'admin.permissions.capability' | transloco }}</th>
              <th *ngFor="let role of matrix.roles" class="role">
                <div class="role-name" [class.pinned]="role.isPinned">{{ role.name }}</div>
                <div class="role-tag" *ngIf="role.isPinned">{{ 'admin.permissions.pinned' | transloco }}</div>
                <div class="role-tag" *ngIf="!role.isPinned && role.isSeeded">
                  {{ 'admin.permissions.builtIn' | transloco }}
                </div>
                <button *ngIf="!role.isSeeded" mat-icon-button class="role-delete"
                        [matTooltip]="'admin.permissions.deleteRole' | transloco"
                        [attr.aria-label]="'admin.permissions.deleteRole' | transloco"
                        (click)="deleteRole(role)" [disabled]="saving">
                  <mat-icon>delete</mat-icon>
                </button>
              </th>
            </tr>
          </thead>
          <tbody>
            <ng-container *ngFor="let group of groups">
              <tr class="group-row">
                <td [attr.colspan]="matrix.roles.length + 1">{{ group.titleKey | transloco }}</td>
              </tr>
              <tr *ngFor="let permission of group.permissions">
                <td class="capability">
                  <div class="capability-name">{{ labelKey(permission) | transloco }}</div>
                  <div class="capability-hint">{{ hintKey(permission) | transloco }}</div>
                </td>
                <td *ngFor="let role of matrix.roles" class="cell">
                  <mat-checkbox
                    [checked]="holds(role, permission)"
                    [disabled]="role.isPinned || saving"
                    (change)="toggle(role, permission, $event.checked)"
                    [attr.aria-label]="role.name + ': ' + (labelKey(permission) | transloco)">
                  </mat-checkbox>
                </td>
              </tr>
            </ng-container>
          </tbody>
        </table>
      </div>

      <div class="actions">
        <span class="dirty-note" *ngIf="dirtyRoleIds.size > 0">
          {{ 'admin.permissions.unsaved' | transloco: { count: dirtyRoleIds.size } }}
        </span>
        <button mat-button (click)="load()" [disabled]="saving || dirtyRoleIds.size === 0">
          {{ 'common.cancel' | transloco }}
        </button>
        <button mat-raised-button color="primary" (click)="save()"
                [disabled]="saving || dirtyRoleIds.size === 0">
          {{ saving ? ('common.saving' | transloco) : ('common.save' | transloco) }}
        </button>
      </div>
    </ng-container>

    <!-- Create a role -->
    <mat-card *ngIf="creating" class="new-role">
      <mat-card-header>
        <mat-card-title>{{ 'admin.permissions.addRole' | transloco }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>{{ 'admin.permissions.roleName' | transloco }}</mat-label>
          <input matInput [(ngModel)]="newRoleName" maxlength="100">
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>{{ 'admin.permissions.roleDescription' | transloco }}</mat-label>
          <input matInput [(ngModel)]="newRoleDescription" maxlength="400">
        </mat-form-field>
        <p class="hint">{{ 'admin.permissions.addRoleHint' | transloco }}</p>
        <div class="actions">
          <button mat-button (click)="creating = false" [disabled]="saving">
            {{ 'common.cancel' | transloco }}
          </button>
          <button mat-raised-button color="primary" (click)="createRole()"
                  [disabled]="saving || !newRoleName.trim()">
            {{ 'common.create' | transloco }}
          </button>
        </div>
      </mat-card-content>
    </mat-card>
    </div>
  `,
  styles: [`
    .toolbar { display: flex; align-items: flex-start; gap: 16px; justify-content: space-between; }
    .hint { color: var(--text-secondary); margin: 0 0 16px; max-width: 80ch; }
    .table-wrapper { overflow-x: auto; }
    .matrix { border-collapse: collapse; width: 100%; }
    .matrix th, .matrix td { padding: 8px 12px; text-align: start; vertical-align: top; }
    .matrix thead th { border-bottom: 2px solid var(--border-color, rgba(128,128,128,.3)); }
    .matrix tbody tr { border-bottom: 1px solid var(--border-color, rgba(128,128,128,.15)); }
    .capability { min-width: 280px; }
    .capability-name { font-weight: 500; }
    .capability-hint { color: var(--text-secondary); font-size: 12px; max-width: 46ch; }
    .role { text-align: center; min-width: 130px; }
    .role-name { font-weight: 500; }
    .role-name.pinned { opacity: .8; }
    .role-tag { color: var(--text-secondary); font-size: 11px; text-transform: uppercase; }
    .cell { text-align: center; }
    .group-row td {
      font-weight: 600; padding-top: 20px;
      color: var(--text-secondary); text-transform: uppercase; font-size: 12px; letter-spacing: .04em;
    }
    .actions { display: flex; justify-content: flex-end; align-items: center; gap: 12px; margin-top: 20px; }
    .dirty-note { color: var(--text-secondary); margin-inline-end: auto; }
    .new-role { margin-top: 16px; }
    .full-width { width: 100%; }
  `]
})
export class PermissionsMatrixComponent implements OnInit {
  matrix: PermissionMatrix | null = null;
  groups = PERMISSION_GROUPS;
  loading = true;
  saving = false;

  creating = false;
  newRoleName = '';
  newRoleDescription = '';

  /** Roles edited since the last load — only these are sent on save. */
  dirtyRoleIds = new Set<string>();

  labelKey = permissionLabelKey;
  hintKey = permissionHintKey;

  constructor(
    private queryService: QueryService,
    private toast: ToastService,
    private confirmService: ConfirmService,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.dirtyRoleIds.clear();
    this.queryService.getPermissionMatrix().subscribe({
      next: (matrix) => {
        this.matrix = matrix;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'admin.permissions.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  holds(role: RolePermissions, permission: string): boolean {
    return role.permissions.includes(permission);
  }

  toggle(role: RolePermissions, permission: string, checked: boolean): void {
    if (role.isPinned) return;

    role.permissions = checked
      ? [...role.permissions, permission]
      : role.permissions.filter(p => p !== permission);

    this.dirtyRoleIds.add(role.id);
  }

  save(): void {
    if (!this.matrix || this.dirtyRoleIds.size === 0) return;

    const changed = this.matrix.roles.filter(r => this.dirtyRoleIds.has(r.id));
    this.saving = true;

    forkJoin(changed.map(r => this.queryService.setRolePermissions(r.id, r.permissions))).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.permissions.saved');
        // Re-read rather than trust the local copy: the server ignores unknown names and pins
        // Admin, so what it stored is the truth worth showing.
        this.load();
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.permissions.saveFailed');
        this.load();
      }
    });
  }

  startCreate(): void {
    this.newRoleName = '';
    this.newRoleDescription = '';
    this.creating = true;
  }

  createRole(): void {
    const name = this.newRoleName.trim();
    if (!name) return;

    this.saving = true;
    this.queryService.createRole({
      name,
      description: this.newRoleDescription.trim() || null,
      permissions: []
    }).subscribe({
      next: () => {
        this.saving = false;
        this.creating = false;
        this.toast.success('admin.permissions.roleCreated');
        this.load();
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.permissions.roleCreateFailed');
        this.cdr.detectChanges();
      }
    });
  }

  deleteRole(role: RolePermissions): void {
    this.confirmService.askThen({
      titleKey: 'admin.permissions.deleteRoleTitle',
      messageKey: 'admin.permissions.deleteRoleMessage',
      params: { name: role.name },
      confirmText: this.transloco.translate('common.delete'),
      destructive: true
    }, () => {
      this.saving = true;
      this.queryService.deleteRole(role.id).subscribe({
        next: () => {
          this.saving = false;
          this.toast.success('admin.permissions.roleDeleted');
          this.load();
        },
        error: (err) => {
          this.saving = false;
          // The server refuses while anyone still holds the role; its message names the count.
          this.toast.error(err, 'admin.permissions.roleDeleteFailed');
          this.cdr.detectChanges();
        }
      });
    });
  }
}
