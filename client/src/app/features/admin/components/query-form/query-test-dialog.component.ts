import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { timeout } from 'rxjs/operators';
import { QueryService } from '@core/services/query.service';
import { extractApiError, extractValidationErrors } from '@core/services/toast.service';
import {
  DropdownSourceType, ParameterType, QueryParameter, QueryTestResult, QueryType, TestQueryRequest
} from '@core/models/dynamic-query.model';

export interface QueryTestDialogData {
  /** A copy of the form as edited — the dialog never writes back to it. */
  request: Omit<TestQueryRequest, 'values'>;
}

interface StaticOption { label: string; value: string; }

/**
 * Runs the query being edited without saving it. Asks for a value per parameter (pre-filled
 * with each default), then shows the first rows of a SELECT, or what an INSERT/UPDATE/DELETE
 * would affect — the server always rolls those back. MERGE, DDL and PL/SQL are refused there.
 */
@Component({
  standalone: true,
  selector: 'app-query-test-dialog',
  imports: [
    CommonModule, ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatProgressSpinnerModule, MatSelectModule, TranslocoModule
  ],
  template: `
    <h2 mat-dialog-title>{{ 'admin.queryTest.title' | transloco }}</h2>
    <mat-dialog-content>
      <p class="intro">{{ 'admin.queryTest.intro' | transloco }}</p>

      <form *ngIf="parameters.length" [formGroup]="values" class="values" (ngSubmit)="run()">
        <ng-container *ngFor="let p of parameters">
          <mat-form-field appearance="outline" class="value-field">
            <mat-label>{{ p.displayName || p.name }}{{ p.isRequired ? ' *' : '' }}</mat-label>

            <mat-select *ngIf="p.parameterType === types.Boolean" [formControlName]="p.name">
              <mat-option value="">—</mat-option>
              <mat-option value="true">{{ 'admin.queryTest.yes' | transloco }}</mat-option>
              <mat-option value="false">{{ 'admin.queryTest.no' | transloco }}</mat-option>
            </mat-select>

            <mat-select *ngIf="staticOptions[p.name] as options" [formControlName]="p.name" [multiple]="!!p.allowMultiple">
              <mat-option *ngFor="let o of options" [value]="o.value">{{ o.label }}</mat-option>
            </mat-select>

            <input *ngIf="p.parameterType !== types.Boolean && !staticOptions[p.name]" matInput
                   [type]="p.parameterType === types.Number ? 'number' : p.parameterType === types.Date ? 'date' : 'text'"
                   [formControlName]="p.name" dir="auto">

            <mat-hint *ngIf="p.allowMultiple && !staticOptions[p.name]">{{ 'admin.queryTest.commaSeparated' | transloco }}</mat-hint>
            <mat-hint *ngIf="isLookupDropdown(p)">{{ 'admin.queryTest.lookupAsText' | transloco }}</mat-hint>
          </mat-form-field>
        </ng-container>
      </form>

      <div *ngIf="running" class="running"><mat-spinner diameter="32"></mat-spinner></div>

      <div *ngIf="errors.length" class="test-error" role="alert">
        <mat-icon>error_outline</mat-icon>
        <div>
          <strong>{{ 'admin.queryTest.failed' | transloco }}</strong>
          <ul><li *ngFor="let e of errors">{{ e }}</li></ul>
        </div>
      </div>

      <ng-container *ngIf="result && !running">
        <ng-container *ngIf="result.queryType === queryTypes.Select; else writeResult">
          <p class="summary" role="status">
            {{ (result.isLimitReached ? 'admin.queryTest.rowsLimited' : 'admin.queryTest.rows')
               | transloco: { count: result.rows.length, limit: result.rowLimit, ms: result.durationMs } }}
          </p>
          <ng-container *ngTemplateOutlet="grid; context: { columns: result.columns, rows: result.rows }"></ng-container>
        </ng-container>

        <ng-template #writeResult>
          <p class="summary write" role="status">
            <mat-icon>undo</mat-icon>
            {{ 'admin.queryTest.wouldAffect' | transloco: { count: result!.affectedRows, ms: result!.durationMs } }}
          </p>
          <ng-container *ngIf="result!.affectedRowColumns.length">
            <p class="caption">
              {{ (result!.isLimitReached ? 'admin.queryTest.affectedRowsLimited' : 'admin.queryTest.affectedRowsCaption')
                 | transloco: { limit: result!.rowLimit } }}
            </p>
            <ng-container *ngTemplateOutlet="grid; context: { columns: result!.affectedRowColumns, rows: result!.affectedRowValues }"></ng-container>
          </ng-container>
        </ng-template>
      </ng-container>

      <ng-template #grid let-columns="columns" let-rows="rows">
        <p *ngIf="!rows.length" class="no-rows">{{ 'admin.queryTest.noRows' | transloco }}</p>
        <div *ngIf="rows.length" class="grid-wrap">
          <table class="test-grid">
            <thead><tr><th *ngFor="let c of columns">{{ c }}</th></tr></thead>
            <tbody>
              <tr *ngFor="let row of rows">
                <td *ngFor="let c of columns" [class.null]="row[c] === null || row[c] === undefined">{{ display(row[c]) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </ng-template>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close type="button">{{ 'common.close' | transloco }}</button>
      <button mat-raised-button color="primary" type="button" (click)="run()" [disabled]="running">
        <mat-icon>play_arrow</mat-icon> {{ (result || errors.length ? 'admin.queryTest.runAgain' : 'admin.queryTest.run') | transloco }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .intro { color: var(--text-secondary); margin: 0 0 16px; max-width: 70ch; }
    .values { display: flex; flex-wrap: wrap; gap: 0 16px; }
    .value-field { width: 260px; }
    .running { display: flex; justify-content: center; padding: 16px; }
    .test-error {
      display: flex; gap: 12px; align-items: flex-start; margin: 8px 0 16px; padding: 12px 16px;
      background: var(--bg-surface); color: var(--text-primary);
      border: 1px solid var(--border-color); border-inline-start: 4px solid var(--status-error); border-radius: 4px;
    }
    .test-error mat-icon { color: var(--status-error); flex-shrink: 0; }
    .test-error ul { margin: 6px 0 0; padding-inline-start: 20px; }
    .summary { display: flex; align-items: center; gap: 8px; font-weight: 500; margin: 8px 0; }
    .summary.write mat-icon { color: var(--status-info); }
    .caption { color: var(--text-secondary); margin: 4px 0 8px; }
    .no-rows { color: var(--text-secondary); font-style: italic; }
    .grid-wrap { max-height: 50vh; overflow: auto; border: 1px solid var(--border-color); border-radius: 4px; }
    .test-grid { border-collapse: collapse; font-size: 13px; min-width: 100%; }
    .test-grid th {
      position: sticky; top: 0; background: var(--bg-surface); color: var(--text-secondary);
      text-align: start; font-weight: 600; white-space: nowrap;
    }
    .test-grid th, .test-grid td { padding: 6px 12px; border-bottom: 1px solid var(--border-color); }
    .test-grid td { color: var(--text-primary); white-space: nowrap; max-width: 420px; overflow: hidden; text-overflow: ellipsis; }
    .test-grid td.null { color: var(--text-hint); font-style: italic; }
  `]
})
export class QueryTestDialogComponent {
  readonly types = ParameterType;
  readonly queryTypes = QueryType;
  readonly parameters: QueryParameter[];
  /** Fixed-list dropdowns, keyed by parameter name. A lookup-query dropdown has no saved
   *  options to load yet, so it is typed as text instead. */
  readonly staticOptions: Record<string, StaticOption[] | undefined> = {};
  readonly values: FormGroup;

  running = false;
  result: QueryTestResult | null = null;
  errors: string[] = [];

  private readonly data: QueryTestDialogData = inject(MAT_DIALOG_DATA);
  private readonly queryService = inject(QueryService);
  private readonly transloco = inject(TranslocoService);
  private readonly cdr = inject(ChangeDetectorRef);

  constructor() {
    const fb = inject(FormBuilder);
    const data = this.data;
    this.parameters = [...data.request.parameters].sort((a, b) => a.sortOrder - b.sortOrder);
    const controls: Record<string, unknown> = {};
    for (const p of this.parameters) {
      const options = p.parameterType === ParameterType.Dropdown && p.dropdownSourceType === DropdownSourceType.Static
        ? this.parseOptions(p.dropdownStaticValues) : undefined;
      this.staticOptions[p.name] = options;
      const fallback = p.defaultValue ?? '';
      controls[p.name] = [options && p.allowMultiple ? (fallback ? [fallback] : []) : fallback];
    }
    this.values = fb.group(controls);
  }

  isLookupDropdown(p: QueryParameter): boolean {
    return p.parameterType === ParameterType.Dropdown && !this.staticOptions[p.name];
  }

  run(): void {
    if (this.running) return;
    this.running = true;
    this.errors = [];
    this.result = null;
    this.cdr.detectChanges();

    // Generous, because the query's own timeout runs inside it; 0 means no timeout at all.
    const waitMs = Math.max(30, (this.data.request.timeoutSeconds || 300) + 15) * 1000;
    this.queryService.testQuery({ ...this.data.request, values: this.wireValues() })
      .pipe(timeout(waitMs))
      .subscribe({
        next: (result) => {
          this.result = result;
          this.running = false;
          this.cdr.detectChanges();
        },
        error: (err) => {
          const reasons = extractValidationErrors(err).map(r => r.message);
          this.errors = reasons.length
            ? reasons
            : [err?.name === 'TimeoutError'
                ? this.transloco.translate('admin.queryTest.timedOut')
                : extractApiError(err, this.transloco.translate('common.operationFailed'))];
          this.running = false;
          this.cdr.detectChanges();
        }
      });
  }

  /** Cell text: NULL spelled out, everything else as the server sent it. */
  display(value: unknown): string {
    if (value === null || value === undefined) return 'NULL';
    return typeof value === 'object' ? JSON.stringify(value) : String(value);
  }

  /** The same wire format as the run page (query-execute buildParams). */
  private wireValues(): Record<string, string> {
    const out: Record<string, string> = {};
    for (const p of this.parameters) {
      const value = this.values.get(p.name)?.value;
      if (p.allowMultiple) {
        const items = Array.isArray(value)
          ? value
          : String(value ?? '').split(',').map(v => v.trim()).filter(v => v.length > 0);
        out[p.name] = JSON.stringify(items);
      } else {
        out[p.name] = String(value ?? '');
      }
    }
    return out;
  }

  private parseOptions(json: string | undefined): StaticOption[] {
    try {
      const parsed = JSON.parse(json || '[]');
      return Array.isArray(parsed)
        ? parsed.filter(o => o && o.value !== undefined).map(o => ({ value: String(o.value), label: String(o.label ?? o.value) }))
        : [];
    } catch {
      return [];
    }
  }
}
