import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { AbstractControl, FormBuilder, FormControl, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { TranslocoService } from '@jsverse/transloco';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { QueryService } from '@core/services/query.service';
import { AiService } from '@core/services/ai.service';
import { AiStatus } from '@core/models/ai.model';
import { LogRotationResult, SETTING_LIMITS, SystemSettings } from '@core/models/dynamic-query.model';

/** 0 (keep forever) or a whole number of days inside the bounds the server enforces. */
function retentionDays(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  if (value === null || value === '' || value === undefined) return { required: true };
  const n = Number(value);
  const { min, max } = SETTING_LIMITS.logRetentionDays;
  return Number.isInteger(n) && (n === 0 || (n >= min && n <= max)) ? null : { retention: true };
}

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
            <mat-card-title>{{ 'admin.settings.aiTitle' | transloco }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <div class="setting">
              <mat-slide-toggle
                [checked]="settings.aiEnabled !== false"
                [disabled]="saving"
                (change)="setToggle('aiEnabled', $event.checked)">
                {{ 'admin.settings.aiEnabled' | transloco }}
              </mat-slide-toggle>
              <p class="hint">{{ 'admin.settings.aiEnabledHint' | transloco }}</p>
              <p class="hint ai-status" role="status" [attr.data-state]="aiStatusState">
                <mat-icon>{{ aiStatusIcon }}</mat-icon>
                <span>{{ aiStatusText }}</span>
              </p>
            </div>
            <div class="setting">
              <mat-slide-toggle
                [checked]="settings.aiDefaultConnection === true"
                [disabled]="saving"
                (change)="setToggle('aiDefaultConnection', $event.checked)">
                {{ 'admin.settings.aiDefaultConnection' | transloco }}
              </mat-slide-toggle>
              <p class="hint">{{ 'admin.settings.aiDefaultConnectionHint' | transloco }}</p>
            </div>
            <div class="setting">
              <mat-slide-toggle
                [checked]="settings.aiSampleValues === true"
                [disabled]="saving"
                (change)="setToggle('aiSampleValues', $event.checked)">
                {{ 'admin.settings.aiSampleValues' | transloco }}
              </mat-slide-toggle>
              <p class="hint">{{ 'admin.settings.aiSampleValuesHint' | transloco }}</p>
            </div>
            <div class="setting ai-rules">
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'admin.settings.aiInstructions' | transloco }}</mat-label>
                <textarea matInput rows="5" [formControl]="aiRules" dir="auto"
                          [placeholder]="'admin.settings.aiInstructionsPlaceholder' | transloco"></textarea>
                <mat-hint>{{ 'admin.settings.aiInstructionsHint' | transloco }}</mat-hint>
                <mat-hint align="end" dir="ltr">{{ aiRules.value.length }} / {{ aiRulesMax }}</mat-hint>
                <mat-error>{{ 'admin.settings.aiInstructionsTooLong' | transloco: { max: aiRulesMax } }}</mat-error>
              </mat-form-field>
              <div class="actions">
                <button mat-button type="button" (click)="resetAiRules()" [disabled]="saving || aiRules.pristine">
                  {{ 'common.cancel' | transloco }}
                </button>
                <button mat-raised-button color="primary" type="button" (click)="saveAiRules()"
                        [disabled]="saving || aiRules.invalid || aiRules.pristine">
                  {{ saving ? ('common.saving' | transloco) : ('common.save' | transloco) }}
                </button>
              </div>
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

              <div class="setting">
                <mat-form-field appearance="outline" class="number-field">
                  <mat-label>{{ 'admin.settings.sqlMaxLength' | transloco }}</mat-label>
                  <input matInput type="number" formControlName="querySqlMaxLength"
                         [min]="limits.sqlMaxLength.min" [max]="limits.sqlMaxLength.max">
                  <mat-error>
                    {{ 'admin.settings.outOfRange' | transloco:
                       { min: limits.sqlMaxLength.min, max: limits.sqlMaxLength.max } }}
                  </mat-error>
                </mat-form-field>
                <p class="hint">{{ 'admin.settings.sqlMaxLengthHint' | transloco }}</p>
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

        <mat-card>
          <mat-card-header>
            <mat-card-title>{{ 'admin.settings.retentionTitle' | transloco }}</mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <p class="card-intro">{{ 'admin.settings.retentionIntro' | transloco }}</p>
            <form [formGroup]="retentionForm" (ngSubmit)="saveRetention()">
              <div class="setting">
                <mat-form-field appearance="outline" class="number-field">
                  <mat-label>{{ 'admin.settings.executionLogRetention' | transloco }}</mat-label>
                  <input matInput type="number" formControlName="executionLogRetentionDays"
                         min="0" [max]="limits.logRetentionDays.max">
                  <span matTextSuffix>{{ 'admin.settings.days' | transloco }}</span>
                  <mat-error>
                    {{ 'admin.settings.retentionOutOfRange' | transloco:
                       { min: limits.logRetentionDays.min, max: limits.logRetentionDays.max } }}
                  </mat-error>
                </mat-form-field>
                <p class="hint">{{ retentionHint(settings.executionLogRetentionDays, 'admin.settings.executionLogRetentionHint') }}</p>
              </div>

              <div class="setting">
                <mat-form-field appearance="outline" class="number-field">
                  <mat-label>{{ 'admin.settings.systemLogRetention' | transloco }}</mat-label>
                  <input matInput type="number" formControlName="systemLogRetentionDays"
                         min="0" [max]="limits.logRetentionDays.max">
                  <span matTextSuffix>{{ 'admin.settings.days' | transloco }}</span>
                  <mat-error>
                    {{ 'admin.settings.retentionOutOfRange' | transloco:
                       { min: limits.logRetentionDays.min, max: limits.logRetentionDays.max } }}
                  </mat-error>
                </mat-form-field>
                <p class="hint">{{ retentionHint(settings.systemLogRetentionDays, 'admin.settings.systemLogRetentionHint') }}</p>
              </div>

              <div class="actions">
                <button mat-stroked-button type="button" class="rotate-button" (click)="rotateNow()"
                        [disabled]="saving || rotating || !retentionForm.pristine || !hasRetention()"
                        [matTooltip]="(retentionForm.pristine ? 'admin.settings.rotateNowHint'
                                       : 'admin.settings.rotateSaveFirst') | transloco">
                  <mat-icon>autorenew</mat-icon>
                  {{ rotating ? ('admin.settings.rotating' | transloco) : ('admin.settings.rotateNow' | transloco) }}
                </button>
                <span class="spacer"></span>
                <button mat-button type="button" (click)="resetRetention()"
                        [disabled]="saving || retentionForm.pristine">
                  {{ 'common.cancel' | transloco }}
                </button>
                <button mat-raised-button color="primary" type="submit"
                        [disabled]="saving || retentionForm.invalid || retentionForm.pristine">
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
    </div>
  `,
  styles: [`
    mat-card { margin-bottom: 16px; }
    .tab-content { padding-top: 24px; }
    .setting { padding: 8px 0; }
    /* Wide enough for the longest label — a clipped "Row limit for previews and looku…"
       reads as a bug, and these fields hold at most seven digits. */
    .number-field { width: 340px; }
    .actions { display: flex; justify-content: flex-end; align-items: center; gap: 12px; margin-top: 8px; }
    .actions .spacer { flex: 1; }
    .card-intro { color: var(--text-secondary); font-size: 13px; margin: 0 0 12px; max-width: 70ch; }
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
    .ai-status { display: flex; align-items: center; gap: 6px; }
    .ai-rules { margin-top: 12px; }
    .full-width { width: 100%; }
    .ai-status mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
    .ai-status[data-state='ready'] mat-icon { color: var(--status-success); }
    .ai-status[data-state='unreachable'] mat-icon,
    .ai-status[data-state='modelMissing'] mat-icon,
    .ai-status[data-state='failed'] mat-icon { color: var(--status-error); }
  `]
})
export class SystemSettingsComponent implements OnInit {
  settings: SystemSettings | null = null;
  numbersForm!: FormGroup;
  retentionForm!: FormGroup;
  readonly aiRulesMax = 4000;
  /** Written and saved with a button, like the numbers: a half-typed rule should not reach the AI. */
  readonly aiRules = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(4000)] });
  limits = SETTING_LIMITS;
  loading = true;
  saving = false;
  rotating = false;
  errorMessage = '';

  /** The AI service as last checked: null while checking, 'failed' when the check itself failed. */
  aiStatus: AiStatus | 'failed' | null = null;

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private ai: AiService,
    private toast: ToastService,
    private confirm: ConfirmService,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef
  ) {
    this.retentionForm = this.fb.group({
      executionLogRetentionDays: [0, [retentionDays]],
      systemLogRetentionDays: [0, [retentionDays]]
    });
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
      ]],
      querySqlMaxLength: [SETTING_LIMITS.sqlMaxLength.min, [
        Validators.required,
        Validators.min(SETTING_LIMITS.sqlMaxLength.min),
        Validators.max(SETTING_LIMITS.sqlMaxLength.max)
      ]]
    });
  }

  ngOnInit(): void {
    // The route guard requires settings.manage, so there is no half-open state to handle:
    // reaching this page at all means the settings are readable.
    this.load();
  }

  load(): void {
    this.loading = true;
    this.errorMessage = '';
    this.queryService.getSystemSettings().subscribe({
      next: (settings) => {
        this.settings = settings;
        this.resetNumbers();
        this.resetRetention();
        this.resetAiRules();
        this.loading = false;
        this.cdr.detectChanges();
        this.checkAi();
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
  resetAiRules(): void {
    this.aiRules.reset(this.settings?.aiInstructions ?? '');
  }

  saveAiRules(): void {
    if (!this.settings || this.aiRules.invalid) return;
    const next: SystemSettings = { ...this.settings, aiInstructions: this.aiRules.value.trim() };
    this.saving = true;
    this.queryService.updateSystemSettings(next).subscribe({
      next: () => {
        this.settings = next;
        this.resetAiRules();
        this.saving = false;
        this.toast.success('admin.settings.saved');
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.settings.saveFailed');
        this.cdr.detectChanges();
      }
    });
  }

  resetNumbers(): void {
    if (!this.settings) return;
    this.numbersForm.reset({
      sessionAccessTokenMinutes: this.settings.sessionAccessTokenMinutes,
      sessionRefreshTokenDays: this.settings.sessionRefreshTokenDays,
      queryMaxRows: this.settings.queryMaxRows,
      querySqlMaxLength: this.settings.querySqlMaxLength
    });
  }

  /** Puts the retention fields back to what the server last confirmed. */
  resetRetention(): void {
    if (!this.settings) return;
    this.retentionForm.reset({
      executionLogRetentionDays: this.settings.executionLogRetentionDays ?? 0,
      systemLogRetentionDays: this.settings.systemLogRetentionDays ?? 0
    });
  }

  /** The hint under a retention field, saying in words what the saved value does. */
  retentionHint(days: number, key: string): string {
    const current = days > 0
      ? this.transloco.translate('admin.settings.retentionCurrentDays', { days })
      : this.transloco.translate('admin.settings.retentionCurrentForever');
    return `${this.transloco.translate(key)} ${current}`;
  }

  /** Rotation only has something to do once at least one log has a retention period. */
  hasRetention(): boolean {
    return !!this.settings &&
      (this.settings.executionLogRetentionDays > 0 || this.settings.systemLogRetentionDays > 0);
  }

  saveRetention(): void {
    if (!this.settings || this.retentionForm.invalid) return;

    const value = this.retentionForm.value;
    const next: SystemSettings = {
      ...this.settings,
      executionLogRetentionDays: Number(value.executionLogRetentionDays),
      systemLogRetentionDays: Number(value.systemLogRetentionDays)
    };

    this.saving = true;
    this.queryService.updateSystemSettings(next).subscribe({
      next: () => {
        this.settings = next;
        this.resetRetention();
        this.saving = false;
        this.toast.success('admin.settings.retentionSaved');
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.settings.saveFailed');
        this.cdr.detectChanges();
      }
    });
  }

  /**
   * Deletes what the saved retention says is due, now. Confirmed first: unlike saving the
   * setting, this removes rows, and they do not come back.
   */
  rotateNow(): void {
    if (!this.settings || !this.hasRetention()) return;
    this.confirm.askThen({
      titleKey: 'admin.settings.rotateConfirmTitle',
      messageKey: 'admin.settings.rotateConfirmMessage',
      params: {
        execution: this.describeRetention(this.settings.executionLogRetentionDays),
        system: this.describeRetention(this.settings.systemLogRetentionDays)
      },
      confirmText: this.transloco.translate('admin.settings.rotateNow'),
      destructive: true
    }, () => {
      this.rotating = true;
      this.cdr.detectChanges();
      this.queryService.rotateLogs().subscribe({
        next: (result: LogRotationResult) => {
          this.rotating = false;
          this.toast.success('admin.settings.rotated', {
            execution: result.executionLogsDeleted,
            system: result.systemLogsDeleted
          }, 6000);
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.rotating = false;
          this.toast.error(err, 'admin.settings.rotateFailed');
          this.cdr.detectChanges();
        }
      });
    });
  }

  /** Asks the server whether the AI service answers, for the line under the AI toggle. */
  checkAi(): void {
    this.aiStatus = null;
    this.cdr.detectChanges();
    this.ai.getStatus().subscribe({
      next: (status) => { this.aiStatus = status; this.cdr.detectChanges(); },
      error: () => { this.aiStatus = 'failed'; this.cdr.detectChanges(); }
    });
  }

  /** 'checking', 'failed', 'notConfigured', or the server's state. Drives the icon colour. */
  get aiStatusState(): string {
    if (this.aiStatus === null) return 'checking';
    if (this.aiStatus === 'failed') return 'failed';
    return this.aiStatus.configuredOnServer ? this.aiStatus.state : 'notConfigured';
  }

  get aiStatusIcon(): string {
    switch (this.aiStatusState) {
      case 'ready': return 'check_circle';
      case 'unreachable': case 'modelMissing': case 'failed': return 'error_outline';
      case 'checking': return 'hourglass_empty';
      default: return 'info';
    }
  }

  get aiStatusText(): string {
    const model = this.aiStatus && this.aiStatus !== 'failed' ? this.aiStatus.model : '';
    const key = ({
      checking: 'admin.settings.aiStatusChecking',
      failed: 'admin.settings.aiStatusFailed',
      notConfigured: 'admin.settings.aiStatusNotConfigured',
      disabled: 'admin.settings.aiStatusOff',
      ready: 'admin.settings.aiStatusReady',
      unreachable: 'admin.settings.aiStatusUnreachable',
      modelMissing: 'admin.settings.aiStatusModelMissing'
    } as Record<string, string>)[this.aiStatusState] ?? 'admin.settings.aiStatusFailed';
    return this.transloco.translate(key, { model });
  }

  private describeRetention(days: number): string {
    return days > 0
      ? this.transloco.translate('admin.settings.retentionOlderThan', { days })
      : this.transloco.translate('admin.settings.retentionKeptForever');
  }

  /**
   * Writes one toggle optimistically and rolls it back on failure: leaving it showing the
   * value the server rejected would misreport what a role is actually allowed to do.
   */
  setToggle(key: 'directoryEnabled' | 'aiEnabled' | 'aiDefaultConnection' | 'aiSampleValues', enabled: boolean): void {
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
        if (key === 'aiEnabled') this.checkAi();
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
