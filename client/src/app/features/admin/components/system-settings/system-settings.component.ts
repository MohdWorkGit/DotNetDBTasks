import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ToastService } from '@core/services/toast.service';
import { QueryService } from '@core/services/query.service';
import { SETTING_LIMITS, SystemSettings } from '@core/models/dynamic-query.model';
import { AuthService } from '@core/services/auth.service';
import { PERM } from '@core/models/permissions';

/**
 * Admin-only runtime settings.
 *
 * <p>Kept separate from the user and role pages: these change what a whole role is permitted
 * to do, or how the server behaves for everyone, which is a different kind of decision from
 * granting one person one query.</p>
 *
 * <p>Toggles save the moment they are flipped — there is nothing to get wrong. Numbers do not:
 * a half-typed "5" on the way to "500" would otherwise be saved and enforced, so they are
 * edited in a form and committed with a button.</p>
 */
@Component({
  standalone: false,
  selector: 'app-system-settings',
  template: `
    <div class="container">
      <div class="header">
        <h2>{{ 'admin.settings.title' | transloco }}</h2>
      </div>

      <mat-tab-group>
        <mat-tab [label]="'admin.settings.systemTab' | transloco" *ngIf="canManageSettings">
        <div class="tab-content">

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <ng-container *ngIf="!loading && settings">
        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ 'admin.settings.directoryTitle' | transloco }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <div class="setting">
              <mat-slide-toggle
                [checked]="settings.directoryEnabled"
                [disabled]="saving"
                (change)="setToggle('directoryEnabled', $event.checked)">
                {{ 'admin.settings.directoryEnabled' | transloco }}
              </mat-slide-toggle>
              <p class="hint">{{ 'admin.settings.directoryEnabledHint' | transloco }}</p>
            </div>
          </mat-card-content>
        </mat-card>

        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ 'admin.settings.limitsTitle' | transloco }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <form [formGroup]="numbersForm" (ngSubmit)="saveNumbers()">
              <div class="setting">
                <mat-form-field appearance="outline" class="number-field">
                  <mat-label>{{ 'admin.settings.accessTokenMinutes' | transloco }}</mat-label>
                  <input matInput type="number" formControlName="sessionAccessTokenMinutes"
                         [min]="limits.accessTokenMinutes.min" [max]="limits.accessTokenMinutes.max">
                  <mat-error>
                    {{ 'admin.settings.outOfRange' | transloco:
                       { min: limits.accessTokenMinutes.min, max: limits.accessTokenMinutes.max } }}
                  </mat-error>
                </mat-form-field>
                <p class="hint">{{ 'admin.settings.accessTokenMinutesHint' | transloco }}</p>
              </div>

              <div class="setting">
                <mat-form-field appearance="outline" class="number-field">
                  <mat-label>{{ 'admin.settings.refreshTokenDays' | transloco }}</mat-label>
                  <input matInput type="number" formControlName="sessionRefreshTokenDays"
                         [min]="limits.refreshTokenDays.min" [max]="limits.refreshTokenDays.max">
                  <mat-error>
                    {{ 'admin.settings.outOfRange' | transloco:
                       { min: limits.refreshTokenDays.min, max: limits.refreshTokenDays.max } }}
                  </mat-error>
                </mat-form-field>
                <p class="hint">{{ 'admin.settings.refreshTokenDaysHint' | transloco }}</p>
              </div>

              <div class="setting">
                <mat-form-field appearance="outline" class="number-field">
                  <mat-label>{{ 'admin.settings.maxRows' | transloco }}</mat-label>
                  <input matInput type="number" formControlName="queryMaxRows"
                         [min]="limits.maxRows.min" [max]="limits.maxRows.max">
                  <mat-error>
                    {{ 'admin.settings.outOfRange' | transloco:
                       { min: limits.maxRows.min, max: limits.maxRows.max } }}
                  </mat-error>
                </mat-form-field>
                <p class="hint">{{ 'admin.settings.maxRowsHint' | transloco }}</p>
              </div>

              <div class="actions">
                <button mat-button type="button" (click)="resetNumbers()"
                        [disabled]="saving || numbersForm.pristine">
                  {{ 'common.cancel' | transloco }}
                </button>
                <button mat-raised-button color="primary" type="submit"
                        [disabled]="saving || numbersForm.invalid || numbersForm.pristine">
                  {{ saving ? ('common.saving' | transloco) : ('common.save' | transloco) }}
                </button>
              </div>
            </form>
          </mat-card-content>
        </mat-card>
      </ng-container>

      <div *ngIf="!loading && errorMessage" class="error-block">
        <p class="error-text">{{ errorMessage }}</p>
        <button mat-stroked-button (click)="load()">{{ 'common.retry' | transloco }}</button>
      </div>
        </div>
        </mat-tab>

        <!-- Who may do what. Its own tab because it answers a different question from the
             switches beside it: not how the system behaves, but who it answers to. -->
        <mat-tab [label]="'admin.permissions.tab' | transloco" *ngIf="canManageRoles">
          <div class="tab-content">
            <app-permissions-matrix></app-permissions-matrix>
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    mat-card { margin-bottom: 16px; }
    .tab-content { padding-top: 24px; }
    .setting { padding: 8px 0; }
    /* Wide enough for the longest label — a clipped "Row limit for previews and looku…"
       reads as a bug, and these fields hold at most seven digits. */
    .number-field { width: 340px; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 8px; }
    .hint {
      color: var(--text-secondary);
      font-size: 13px;
      margin: 8px 0 0;
      /* Lines up under the toggle's label rather than its track. */
      padding-inline-start: 52px;
      max-width: 70ch;
    }
    /* A number field has no track to clear, so its hint sits flush. */
    .number-field + .hint { padding-inline-start: 0; }
  `]
})
export class SystemSettingsComponent implements OnInit {
  settings: SystemSettings | null = null;
  /** Each tab needs its own capability — the page opens for either one. */
  canManageSettings = false;
  canManageRoles = false;
  numbersForm!: FormGroup;
  limits = SETTING_LIMITS;
  loading = true;
  saving = false;
  errorMessage = '';

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private toast: ToastService,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {
    this.numbersForm = this.fb.group({
      sessionAccessTokenMinutes: [SETTING_LIMITS.accessTokenMinutes.min, [
        Validators.required,
        Validators.min(SETTING_LIMITS.accessTokenMinutes.min),
        Validators.max(SETTING_LIMITS.accessTokenMinutes.max)
      ]],
      sessionRefreshTokenDays: [SETTING_LIMITS.refreshTokenDays.min, [
        Validators.required,
        Validators.min(SETTING_LIMITS.refreshTokenDays.min),
        Validators.max(SETTING_LIMITS.refreshTokenDays.max)
      ]],
      queryMaxRows: [SETTING_LIMITS.maxRows.min, [
        Validators.required,
        Validators.min(SETTING_LIMITS.maxRows.min),
        Validators.max(SETTING_LIMITS.maxRows.max)
      ]]
    });
  }

  ngOnInit(): void {
    this.canManageSettings = this.authService.has(PERM.settingsManage);
    this.canManageRoles = this.authService.has(PERM.rolesManage);
    if (this.canManageSettings) {
      this.load();
    } else {
      this.loading = false;
    }
  }

  load(): void {
    this.loading = true;
    this.errorMessage = '';
    this.queryService.getSystemSettings().subscribe({
      next: (settings) => {
        this.settings = settings;
        this.resetNumbers();
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || '';
        this.toast.error(err, 'admin.settings.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  /** Puts the form back to what the server last confirmed. */
  resetNumbers(): void {
    if (!this.settings) return;
    this.numbersForm.reset({
      sessionAccessTokenMinutes: this.settings.sessionAccessTokenMinutes,
      sessionRefreshTokenDays: this.settings.sessionRefreshTokenDays,
      queryMaxRows: this.settings.queryMaxRows
    });
  }

  /**
   * Writes one toggle optimistically and rolls it back on failure: leaving it showing the
   * value the server rejected would misreport what a role is actually allowed to do.
   */
  setToggle(key: 'directoryEnabled', enabled: boolean): void {
    if (!this.settings) return;

    const previous = this.settings[key];
    const next: SystemSettings = { ...this.settings, [key]: enabled };

    this.saving = true;
    this.queryService.updateSystemSettings(next).subscribe({
      next: () => {
        this.settings = next;
        this.saving = false;
        this.toast.success('admin.settings.saved');
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.settings = { ...next, [key]: previous };
        this.saving = false;
        this.toast.error(err, 'admin.settings.saveFailed');
        this.cdr.detectChanges();
      }
    });
  }

  saveNumbers(): void {
    if (!this.settings || this.numbersForm.invalid) return;

    // The whole object goes back, so the toggles must ride along at their current values.
    const next: SystemSettings = { ...this.settings, ...this.numbersForm.value };

    this.saving = true;
    this.queryService.updateSystemSettings(next).subscribe({
      next: () => {
        this.settings = next;
        this.numbersForm.markAsPristine();
        this.saving = false;
        this.toast.success('admin.settings.saved');
        this.cdr.detectChanges();
      },
      error: (err) => {
        // The server has its own bounds and a rule the form cannot express (a refresh token
        // shorter than the access token). Its message is the useful one; keep what was typed
        // so it can be corrected rather than retyped.
        this.saving = false;
        this.toast.error(err, 'admin.settings.saveFailed');
        this.cdr.detectChanges();
      }
    });
  }
}
