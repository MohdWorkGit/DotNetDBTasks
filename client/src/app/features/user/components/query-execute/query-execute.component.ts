import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { PageEvent } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { ToastService } from '@core/services/toast.service';
import { forkJoin, of, throwError, Subject, Subscription } from 'rxjs';
import { catchError, debounceTime, switchMap, timeout } from 'rxjs/operators';
import { ExportFormat, QueryService } from '@core/services/query.service';
import { AuthService } from '@core/services/auth.service';
import { EXPORT_FORMATS, ExportFormatOption } from '@core/models/export-formats';
import { TranslocoService } from '@jsverse/transloco';
import {
  DropdownOption,
  DynamicQuery,
  ExecuteResult,
  isWriteQueryType,
  ParameterType,
  QueryParameter
} from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-query-execute',
  template: `
    <div class="container">
      <div *ngIf="loadingQuery" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <mat-card *ngIf="!loadingQuery && queryError" class="error-card">
        <mat-card-content>
          <p>{{ queryError }}</p>
          <button mat-raised-button color="primary" (click)="loadQuery()">
            <mat-icon>refresh</mat-icon> {{ 'common.retry' | transloco }}
          </button>
        </mat-card-content>
      </mat-card>

      <h2 *ngIf="query" dir="auto">{{ query.name }}</h2>
      <p *ngIf="query" class="description" dir="auto">{{ query.description }}</p>

      <mat-card *ngIf="query">
        <mat-card-header>
          <mat-card-title>{{ 'user.execute.parameters' | transloco }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <div *ngIf="loadingDropdowns" class="loading-hint">
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
            <p>{{ 'user.execute.loadingOptions' | transloco }}</p>
          </div>

          <form [formGroup]="form" (ngSubmit)="execute()">
            <div class="form-grid">
              <ng-container *ngFor="let param of sortedParameters">
                <!-- String (single value, or comma-separated when allowMultiple) -->
                <mat-form-field *ngIf="param.parameterType === 0" appearance="outline">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput [formControlName]="param.name"
                         [placeholder]="param.allowMultiple ? 'value1, value2, value3' : ''">
                  <mat-hint *ngIf="param.allowMultiple">
                    {{ 'user.execute.multiValueHint' | transloco }}
                  </mat-hint>
                  <mat-error *ngIf="form.get(param.name)?.hasError('required')">
                    {{ 'common.fieldRequired' | transloco: { field: param.displayName } }}
                  </mat-error>
                  <mat-error *ngIf="form.get(param.name)?.hasError('sqlInjection')">
                    {{ 'user.execute.sqlInjection' | transloco }}
                  </mat-error>
                </mat-form-field>

                <!-- Number -->
                <mat-form-field *ngIf="param.parameterType === 1" appearance="outline">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput type="number" [formControlName]="param.name">
                  <mat-error *ngIf="form.get(param.name)?.hasError('required')">
                    {{ 'common.fieldRequired' | transloco: { field: param.displayName } }}
                  </mat-error>
                </mat-form-field>

                <!-- Date -->
                <mat-form-field *ngIf="param.parameterType === 2" appearance="outline">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput [matDatepicker]="picker" [formControlName]="param.name">
                  <mat-datepicker-toggle matIconSuffix [for]="picker"></mat-datepicker-toggle>
                  <mat-datepicker #picker></mat-datepicker>
                  <mat-error *ngIf="form.get(param.name)?.hasError('required')">
                    {{ 'common.fieldRequired' | transloco: { field: param.displayName } }}
                  </mat-error>
                </mat-form-field>

                <!-- Boolean -->
                <div *ngIf="param.parameterType === 3" class="toggle-field">
                  <mat-slide-toggle [formControlName]="param.name">
                    {{ param.displayName }}
                  </mat-slide-toggle>
                </div>

                <!-- Dropdown (single or multi based on param.allowMultiple) -->
                <mat-form-field *ngIf="param.parameterType === 4" appearance="outline">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <mat-select [formControlName]="param.name" [multiple]="!!param.allowMultiple">
                    <mat-option
                      *ngFor="let opt of dropdownOptions[param.name]"
                      [value]="opt.value">
                      {{ opt.label }}
                    </mat-option>
                  </mat-select>
                  <mat-hint *ngIf="!dropdownOptions[param.name]?.length && !loadingDropdowns">
                    {{ 'user.execute.noOptions' | transloco }}
                  </mat-hint>
                  <mat-error *ngIf="form.get(param.name)?.hasError('required')">
                    {{ 'common.fieldRequired' | transloco: { field: param.displayName } }}
                  </mat-error>
                </mat-form-field>
              </ng-container>
            </div>

            <!-- Write queries normally run a server-side preview first (execute in a
                 transaction, collect the affected rows, roll back) which is two extra
                 round trips and can be slow on large tables. This opts out of it, and
                 is only offered when an admin allows it for this query. -->
            <div *ngIf="allowsSkipPreview" class="skip-preview">
              <mat-checkbox [(ngModel)]="skipPreview" [ngModelOptions]="{ standalone: true }"
                            [disabled]="executing">
                {{ 'user.execute.runWithoutPreview' | transloco }}
              </mat-checkbox>
              <p class="skip-preview-note" [class.armed]="skipPreview">
                <mat-icon inline>{{ skipPreview ? 'warning' : 'info' }}</mat-icon>
                {{ (skipPreview ? 'user.execute.skipPreviewArmed' : 'user.execute.skipPreviewHint') | transloco }}
              </p>
            </div>

            <div class="actions">
              <button mat-raised-button [color]="willSkipPreview ? 'warn' : 'primary'"
                      type="submit"
                      [disabled]="form.invalid || executing || loadingDropdowns">
                <mat-icon>play_arrow</mat-icon>
                {{ executing
                    ? (query.isLongRunning
                        ? ('user.execute.executingElapsed' | transloco: { elapsed: formatElapsed(elapsedSeconds) })
                        : ('user.execute.executing' | transloco))
                    : ((willSkipPreview ? 'user.execute.runAndCommit' : 'user.execute.executeQuery') | transloco) }}
              </button>
              <button mat-stroked-button color="warn" type="button"
                      *ngIf="executing && query.isLongRunning" (click)="cancelExecution()">
                <mat-icon>cancel</mat-icon> {{ 'common.cancel' | transloco }}
              </button>
              <button mat-stroked-button type="button"
                      *ngIf="result && result.columns.length > 0 && result.jobId
                             && availableExportFormats.length > 0"
                      [matMenuTriggerFor]="exportMenu" [disabled]="exporting">
                <mat-icon>download</mat-icon>
                {{ (exporting ? 'user.execute.exporting' : 'user.execute.export') | transloco }}
                <mat-icon iconPositionEnd>arrow_drop_down</mat-icon>
              </button>
              <mat-menu #exportMenu="matMenu">
                <button mat-menu-item *ngFor="let f of availableExportFormats"
                        (click)="exportResults(f.apiValue)">
                  <mat-icon>{{ f.icon }}</mat-icon> {{ f.labelKey | transloco }}
                </button>
              </mat-menu>
            </div>
          </form>
        </mat-card-content>
      </mat-card>

      <mat-card *ngIf="pendingPreview" class="confirm-card">
        <mat-card-header>
          <mat-icon mat-card-avatar class="warn-icon">warning</mat-icon>
          <mat-card-title>{{ 'user.execute.confirmTitle' | transloco }}</mat-card-title>
          <mat-card-subtitle>{{ 'user.execute.confirmSubtitle' | transloco }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <p>
            {{ (pendingPreview.affectedRows === 1
                  ? 'user.execute.willAffectOne'
                  : 'user.execute.willAffectMany') | transloco: { count: pendingPreview.affectedRows } }}
          </p>
          <div *ngIf="pendingPreview.previewRows && pendingPreview.previewRows.length > 0"
               class="preview-table-wrapper">
            <p class="preview-table-title">{{ 'user.execute.rowsAffectedTitle' | transloco }}</p>
            <table mat-table [dataSource]="pendingPreview.previewRows" class="preview-table">
              <ng-container *ngFor="let col of pendingPreview.previewColumns || []" [matColumnDef]="col">
                <th mat-header-cell *matHeaderCellDef>{{ col }}</th>
                <td mat-cell *matCellDef="let row" dir="auto">{{ row[col] }}</td>
              </ng-container>
              <tr mat-header-row *matHeaderRowDef="pendingPreview.previewColumns || []"></tr>
              <tr mat-row *matRowDef="let row; columns: pendingPreview.previewColumns || [];"></tr>
            </table>
          </div>
        </mat-card-content>
        <mat-card-actions align="end">
          <button mat-stroked-button type="button" (click)="cancelConfirm()" [disabled]="executing">
            {{ 'common.cancel' | transloco }}
          </button>
          <button mat-raised-button color="warn" type="button" (click)="confirmExecute()" [disabled]="executing">
            <mat-icon>check</mat-icon>
            {{ (executing ? 'user.execute.committing' : 'user.execute.confirmAndCommit') | transloco }}
          </button>
        </mat-card-actions>
      </mat-card>

      <mat-card *ngIf="result" class="results-card">
        <mat-card-header>
          <mat-card-title>{{ 'user.execute.results' | transloco }}</mat-card-title>
          <mat-card-subtitle>
            <span *ngIf="result.columns.length > 0">
              {{ 'user.execute.rowsReturned' | transloco: { rows: result.totalRows, ms: result.executionDurationMs } }}<span
                *ngIf="filteredTotal !== result.totalRows"> · {{ 'user.execute.matchFilter' | transloco: { count: filteredTotal } }}</span>
            </span>
            <span *ngIf="result.columns.length === 0">
              {{ 'user.execute.rowsAffectedIn' | transloco: { rows: result.affectedRows, ms: result.executionDurationMs } }}
            </span>
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div *ngIf="result.isLimitReached" class="limit-warning">
            <mat-icon>warning_amber</mat-icon>
            <span>
              {{ 'user.execute.displayLimit' | transloco: { rows: result.totalRows } }}
            </span>
          </div>

          <mat-progress-bar *ngIf="loadingRows" mode="indeterminate"></mat-progress-bar>

          <div *ngIf="result.columns.length > 0" class="table-wrapper">
            <table mat-table [dataSource]="pageRows" matSort (matSortChange)="onSortChange($event)">
              <ng-container *ngFor="let col of result.columns" [matColumnDef]="col">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ col }}</th>
                <td mat-cell *matCellDef="let row" dir="auto">{{ row[col] }}</td>
              </ng-container>

              <ng-container *ngFor="let col of result.columns" [matColumnDef]="'filter_' + col">
                <th mat-header-cell *matHeaderCellDef>
                  <input class="col-filter-input"
                         [value]="columnFilters[col] || ''"
                         [attr.placeholder]="'user.execute.filterPlaceholder' | transloco"
                         [attr.aria-label]="'user.execute.filterBy' | transloco: { column: col }"
                         (input)="applyColumnFilter($event, col)" />
                </th>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="result.columns"></tr>
              <tr mat-header-row *matHeaderRowDef="filterColumns" class="filter-row"></tr>
              <tr mat-row *matRowDef="let row; columns: result.columns;"></tr>

              <tr class="mat-row no-data-row" *matNoDataRow>
                <td class="mat-cell no-data-cell" [attr.colspan]="result.columns.length">
                  {{ (hasColumnFilters()
                      ? 'user.execute.noRowsMatchFilters'
                      : 'user.execute.noRows') | transloco }}
                </td>
              </tr>
            </table>
          </div>

          <div *ngIf="result.columns.length === 0" class="non-query-result">
            <mat-icon>check_circle</mat-icon>
            <p>{{ 'user.execute.successAffected' | transloco: { rows: result.affectedRows } }}</p>
          </div>

          <mat-paginator *ngIf="result.columns.length > 0"
                         [length]="filteredTotal"
                         [pageIndex]="pageIndex"
                         [pageSize]="pageSize"
                         [pageSizeOptions]="[10, 25, 50, 100]"
                         (page)="onPage($event)"
                         showFirstLastButtons>
          </mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .error-card { margin-bottom: 16px; }
    .error-card p { color: var(--status-error); margin-bottom: 16px; }
    .description { color: var(--text-secondary); margin-bottom: 16px; }
    .form-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
      gap: 16px;
    }
    .toggle-field { display: flex; align-items: center; padding: 16px 0; }
    .actions { display: flex; gap: 12px; margin-top: 16px; }
    .results-card { margin-top: 24px; }
    table { width: 100%; }
    /* Matches the sort-header row above it (--bg-surface); using --bg-secondary here
       made the filter row read as a separate darker band in dark mode only. */
    .filter-row th { padding-top: 4px; padding-bottom: 4px; background: var(--bg-surface); }
    .col-filter-input {
      width: 100%;
      box-sizing: border-box;
      border: 1px solid var(--border-color);
      border-radius: 4px;
      padding: 4px 6px;
      font-size: 12px;
      background: var(--bg-primary);
      color: inherit;
      outline: none;
    }
    .col-filter-input:focus { border-color: var(--accent-primary); box-shadow: 0 0 0 2px rgba(99,102,241,.25); }
    .col-filter-input::placeholder { color: var(--text-hint); }
    .limit-warning {
      display: flex; align-items: center; gap: 8px;
      padding: 10px 14px; margin-bottom: 12px;
      border-radius: 4px;
      background: var(--status-warning-bg);
      border: 1px solid var(--status-warning);
      color: var(--status-warning-text);
      font-size: 13px;
    }
    .limit-warning mat-icon { color: var(--status-warning); flex-shrink: 0; }
    .non-query-result { display: flex; align-items: center; gap: 8px; padding: 24px 0; color: var(--status-success); }
    .non-query-result mat-icon { font-size: 32px; width: 32px; height: 32px; }
    .non-query-result p { font-size: 16px; margin: 0; }
    .loading-hint { margin-bottom: 16px; }
    .loading-hint p { margin-top: 8px; font-size: 13px; color: var(--text-secondary); }
    .skip-preview { margin: 4px 0 12px; }
    .skip-preview-note {
      margin: 4px 0 0 32px;
      font-size: 12px;
      color: var(--text-secondary);
      display: flex;
      align-items: center;
      gap: 6px;
    }
    .skip-preview-note.armed { color: var(--status-warning); font-weight: 500; }
    .confirm-card { margin-top: 16px; border-inline-start: 4px solid var(--status-warning); }
    .confirm-card .warn-icon {
      display: flex; align-items: center; justify-content: center;
      background: var(--status-warning); color: #fff; border-radius: 50%;
    }
    .confirm-card p { font-size: 15px; margin: 0; }
    .preview-table-wrapper { margin-top: 12px; overflow-x: auto; max-height: 320px; overflow-y: auto; border: 1px solid var(--border-color); border-radius: 4px; }
    .preview-table-title { font-size: 13px; margin: 0 0 8px 0; color: var(--text-secondary); }
    .preview-table { width: 100%; font-size: 13px; }
    .preview-table th { font-weight: 600; background: var(--bg-secondary); position: sticky; top: 0; z-index: 1; }
  `]
})
export class QueryExecuteComponent implements OnInit, OnDestroy {
  query?: DynamicQuery;
  form!: FormGroup;
  /** Execution metadata for the current result (columns, totals, jobId); rows are paged separately. */
  result?: ExecuteResult;
  /** Preview returned by the backend for a write query awaiting user confirmation. */
  /** Opt-out of the server-side preview for write queries; resets on every page load. */
  skipPreview = false;
  pendingPreview?: ExecuteResult;
  /** Parameters used for the pending preview, replayed on confirm. */
  private pendingParams: Record<string, string> = {};

  /** The current page of rows for the results grid, fetched from the cached job server-side. */
  pageRows: Record<string, any>[] = [];
  /** Job id of the cached read result currently displayed; drives paging and export. */
  private resultJobId?: string;
  pageIndex = 0;
  pageSize = 25;
  /** Rows matching the active filters (paginator length). Equals totalRows when unfiltered. */
  filteredTotal = 0;
  private sortColumn?: string;
  private sortDir = '';
  loadingRows = false;

  executing = false;
  exporting = false;
  loadingQuery = true;
  loadingDropdowns = false;
  queryError = '';
  columnFilters: Record<string, string> = {};
  filterColumns: string[] = [];
  /** Debounces column-filter keystrokes into a single paged request. */
  private filterChange$ = new Subject<void>();

  /** Seconds elapsed since the current execution started, shown on the button. */
  elapsedSeconds = 0;
  /** Wall-clock start of the current execution; elapsedSeconds is derived from it. */
  private startedAt = 0;
  /** Job id of the in-flight execution, used to cancel it server-side. */
  private currentJobId?: string;
  private pollSub?: Subscription;
  private exportSub?: Subscription;
  private rowsSub?: Subscription;
  private filterSub?: Subscription;
  private timerHandle?: any;

  /** Maps param.name -> list of dropdown options */
  dropdownOptions: Record<string, DropdownOption[]> = {};

  /** Parameters in display order (by sortOrder), matching the admin settings page. */
  get sortedParameters(): QueryParameter[] {
    return [...(this.query?.parameters ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);
  }

  private queryId = '';

  /**
   * The export formats this person may actually download for this query: the formats the query
   * permits, narrowed to the ones their roles hold. Empty hides the Export button entirely,
   * which is the normal state for a query nobody has opened export on.
   *
   * <p>Recomputed when the query loads rather than evaluated in the template — an *ngFor over a
   * method call would re-filter on every change-detection pass.</p>
   */
  availableExportFormats: ExportFormatOption[] = [];

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private toast: ToastService,
    private transloco: TranslocoService,
    private authService: AuthService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({});
    this.queryId = this.route.snapshot.params['id'];
    // Coalesce filter keystrokes: reset to the first page and reload once typing settles.
    this.filterSub = this.filterChange$.pipe(debounceTime(300)).subscribe(() => {
      this.pageIndex = 0;
      this.loadPage();
    });
    this.loadQuery();
  }

  loadQuery(): void {
    this.loadingQuery = true;
    this.queryError = '';

    this.queryService.getMyQueryById(this.queryId).pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out. Please try again.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (query) => {
        this.query = query;
        this.loadingQuery = false;

        const permitted = query.allowedExportFormats ?? [];
        this.availableExportFormats = EXPORT_FORMATS.filter(
          f => permitted.includes(f.name) && this.authService.has(f.permission));

        // Build dynamic form from parameter metadata
        const sorted = [...this.query.parameters].sort((a, b) => a.sortOrder - b.sortOrder);
        for (const param of sorted) {
          const isMultiDropdown = param.parameterType === ParameterType.Dropdown && !!param.allowMultiple;
          const validators: any[] = param.isRequired ? [Validators.required] : [];
          if (param.parameterType === ParameterType.String) {
            validators.push(this.sqlInjectionValidator);
          }
          if (isMultiDropdown && param.isRequired) {
            // Required-on-array means at least one selection.
            validators.push(this.nonEmptyArrayValidator);
          }
          let defaultValue: any = param.defaultValue || '';

          if (param.parameterType === ParameterType.Boolean) {
            defaultValue = param.defaultValue === 'true';
          } else if (isMultiDropdown) {
            defaultValue = [];
          }

          this.form.addControl(param.name, this.fb.control(defaultValue, validators));
        }

        // Load options for all dropdown parameters (single- and multi-select) in parallel
        const dropdownParams = sorted.filter(p => p.parameterType === ParameterType.Dropdown && p.id);
        if (dropdownParams.length > 0) {
          this.loadingDropdowns = true;
          const requests = dropdownParams.map(p =>
            this.queryService.getDropdownOptions(this.queryId, p.id!).pipe(
              catchError(() => of([] as DropdownOption[]))
            )
          );

          forkJoin(requests).subscribe(results => {
            dropdownParams.forEach((p, idx) => {
              this.dropdownOptions[p.name] = results[idx];
            });
            this.loadingDropdowns = false;
            this.cdr.detectChanges();
          });
        }

        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingQuery = false;
        this.queryError = err.error?.message || this.transloco.translate('user.execute.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  private nonEmptyArrayValidator(control: AbstractControl): ValidationErrors | null {
    return Array.isArray(control.value) && control.value.length > 0 ? null : { required: true };
  }

  private sqlInjectionValidator(control: AbstractControl): ValidationErrors | null {
    const value = String(control.value ?? '').trim();
    if (!value) return null;

    const patterns = [
      /--/,                                                                            // SQL line comment
      /\/\*/,                                                                          // SQL block comment
      /;\s*(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|TRUNCATE|EXEC|EXECUTE)\b/i, // stacked queries
      /\bUNION\b.{0,30}\bSELECT\b/i,                                                // UNION SELECT
      /\b(OR|AND)\s+['"]?\d+['"]?\s*=\s*['"]?\d+['"]?/i,                           // OR 1=1 / AND 1=1
      /'\s*(OR|AND)\s+'[^']*'\s*=\s*'/i,                                            // ' OR 'x'='x
      /\bEXEC(\s+|\s*\()\s*(xp_|sp_)/i,                                             // EXEC xp_ / sp_
      /\bWAITFOR\s+DELAY\b/i,                                                        // time-based blind (MSSQL)
      /\bSLEEP\s*\(/i,                                                               // time-based blind (MySQL)
    ];

    return patterns.some(p => p.test(value)) ? { sqlInjection: true } : null;
  }

  /**
   * True when the query modifies data, so the preview/confirm step applies. Reads the type the
   * server derived and stored at save time rather than re-parsing the SQL here.
   */
  get isWriteQuery(): boolean {
    return isWriteQueryType(this.query?.queryType);
  }

  /**
   * Whether the "run without preview" opt-out is offered at all: only for write queries, and
   * only when an admin enabled it for this query on the manage-query page.
   */
  get allowsSkipPreview(): boolean {
    return this.isWriteQuery && this.query?.allowRunWithoutConfirmation !== false;
  }

  /** Whether this run will actually commit without a confirmation step. */
  get willSkipPreview(): boolean {
    return this.allowsSkipPreview && this.skipPreview;
  }

  execute(): void {
    if (this.form.invalid || !this.query) return;

    this.pendingPreview = undefined;
    // Sending confirmed=true up front makes the server skip the preview round trip
    // entirely and commit in one pass. The checkbox is the deliberate opt-in, so
    // there is no second prompt.
    this.runExecute(this.buildParams(), this.willSkipPreview);
  }

  /** Builds the backend wire-format parameter map from the current form values. */
  private buildParams(): Record<string, string> {
    const params: Record<string, string> = {};
    if (!this.query) return params;

    for (const param of this.query.parameters) {
      let value = this.form.get(param.name)?.value;

      if (param.parameterType === ParameterType.Date && value instanceof Date) {
        value = value.toISOString().split('T')[0];
      } else if (param.parameterType === ParameterType.Boolean) {
        value = String(value);
      } else if (param.parameterType === ParameterType.Dropdown && param.allowMultiple) {
        // Backend wire format is Record<string, string>; multi-select payloads
        // ride along as a JSON-array string and are parsed server-side.
        value = JSON.stringify(Array.isArray(value) ? value : []);
      } else if (param.parameterType === ParameterType.String && param.allowMultiple) {
        // Comma-separated textbox input — split, trim, drop empties, then send in the
        // same JSON-array wire format as multi-select dropdowns.
        const items = String(value ?? '')
          .split(',')
          .map(v => v.trim())
          .filter(v => v.length > 0);
        value = JSON.stringify(items);
      } else {
        value = String(value ?? '');
      }

      params[param.name] = value;
    }

    return params;
  }

  confirmExecute(): void {
    if (!this.pendingPreview) return;
    this.runExecute(this.pendingParams, true);
  }

  cancelConfirm(): void {
    this.pendingPreview = undefined;
    this.pendingParams = {};
    this.cdr.detectChanges();
  }

  private runExecute(params: Record<string, string>, confirmed: boolean): void {
    if (!this.query) return;

    this.executing = true;

    if (this.query.isLongRunning) {
      // Long-running queries run as a background job the page polls for. Each request is short,
      // so they are not cut off by proxy/edge timeouts. A timer and Cancel button are shown.
      this.startTimer();
      this.pollSub = this.queryService.submitQuery(this.query.id, params, confirmed).pipe(
        switchMap(({ jobId }) => {
          this.currentJobId = jobId;
          return this.queryService.pollJobResult(jobId);
        })
      ).subscribe({
        next: (res) => this.handleResult(res, params, confirmed),
        error: (err) => this.handleError(err)
      });
    } else {
      // Normal, quick queries run synchronously and return in a single request — no polling.
      this.pollSub = this.queryService.executeQuery(this.query.id, params, confirmed).subscribe({
        next: (res) => this.handleResult(res, params, confirmed),
        error: (err) => this.handleError(err)
      });
    }
  }

  private handleResult(res: ExecuteResult, params: Record<string, string>, confirmed: boolean): void {
    this.stopExecuting();

    if (res.requiresConfirmation && !confirmed) {
      this.pendingPreview = res;
      this.pendingParams = params;
      this.result = undefined;
      this.cdr.detectChanges();
      return;
    }

    this.pendingPreview = undefined;
    this.pendingParams = {};
    this.result = res;

    // Reset grid state for the new result.
    this.columnFilters = {};
    this.filterColumns = res.columns.map(c => 'filter_' + c);
    this.pageIndex = 0;
    this.sortColumn = undefined;
    this.sortDir = '';
    this.pageRows = [];
    this.filteredTotal = res.totalRows;

    // A new result supersedes the previous cached one — free the old one server-side.
    const previousJobId = this.resultJobId;
    this.resultJobId = res.jobId ?? undefined;
    if (previousJobId && previousJobId !== this.resultJobId) {
      this.queryService.releaseJob(previousJobId).subscribe({ error: () => {} });
    }

    // Read results are cached server-side — load the first page. Non-query results have no rows.
    if (res.columns.length > 0 && this.resultJobId) {
      this.loadPage();
    }
    this.cdr.detectChanges();
  }

  /** Fetches the current page of the cached result with the active sort and column filters. */
  private loadPage(): void {
    if (!this.resultJobId) return;

    this.loadingRows = true;
    this.rowsSub?.unsubscribe();
    this.rowsSub = this.queryService.getJobRows(this.resultJobId, {
      pageIndex: this.pageIndex,
      pageSize: this.pageSize,
      sortColumn: this.sortColumn,
      sortDir: this.sortDir,
      filters: this.columnFilters
    }).subscribe({
      next: (page) => {
        this.pageRows = page.rows;
        this.filteredTotal = page.filteredTotal;
        this.loadingRows = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingRows = false;
        this.toast.error(err, 'user.execute.loadResultsFailed', 6000);
        this.cdr.detectChanges();
      }
    });
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.loadPage();
  }

  onSortChange(sort: Sort): void {
    this.sortColumn = sort.direction ? sort.active : undefined;
    this.sortDir = sort.direction;
    this.pageIndex = 0;
    this.loadPage();
  }

  private handleError(err: any): void {
    this.stopExecuting();
    this.toast.error(err, 'user.execute.executionFailed');
    this.cdr.detectChanges();
  }

  /** Cancels the in-flight execution: stops polling and aborts the query server-side. */
  cancelExecution(): void {
    const jobId = this.currentJobId;
    this.pollSub?.unsubscribe();
    this.stopExecuting();
    if (jobId) {
      this.queryService.cancelJob(jobId).subscribe({
        next: () => this.toast.success('user.execute.canceled'),
        // The client already stopped polling, but the server-side query is still
        // holding a DB connection. Saying "canceled" here would be a lie.
        error: (err) => this.toast.error(err, 'user.execute.cancelFailed', 6000)
      });
    }
    this.cdr.detectChanges();
  }

  private startTimer(): void {
    this.stopTimer();
    this.startedAt = Date.now();
    this.elapsedSeconds = 0;
    // The tick only triggers a repaint — the value comes from the wall clock, because
    // background tabs get their intervals throttled to about one fire per minute and a
    // counter that incremented per tick would drift behind the real execution time.
    this.timerHandle = setInterval(() => this.tickTimer(), 1000);
    // A throttled tab can be up to a minute late, so resync the moment it is visible again.
    document.addEventListener('visibilitychange', this.onVisibilityChange);
  }

  private tickTimer(): void {
    const seconds = Math.floor((Date.now() - this.startedAt) / 1000);
    if (seconds === this.elapsedSeconds) return;
    this.elapsedSeconds = seconds;
    this.cdr.detectChanges();
  }

  private onVisibilityChange = (): void => {
    if (!document.hidden && this.timerHandle) this.tickTimer();
  };

  private stopTimer(): void {
    if (this.timerHandle) {
      clearInterval(this.timerHandle);
      this.timerHandle = undefined;
    }
    document.removeEventListener('visibilitychange', this.onVisibilityChange);
  }

  private stopExecuting(): void {
    this.executing = false;
    this.currentJobId = undefined;
    this.stopTimer();
  }

  formatElapsed(totalSeconds: number): string {
    const m = Math.floor(totalSeconds / 60);
    const s = totalSeconds % 60;
    return `${m}:${s.toString().padStart(2, '0')}`;
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
    this.exportSub?.unsubscribe();
    this.rowsSub?.unsubscribe();
    this.filterSub?.unsubscribe();
    this.filterChange$.complete();
    // Leaving the page: free the cached result now instead of waiting for it to expire.
    if (this.resultJobId) {
      this.queryService.releaseJob(this.resultJobId).subscribe({ error: () => {} });
    }
    this.stopTimer();
  }

  applyColumnFilter(event: Event, col: string): void {
    this.columnFilters[col] = (event.target as HTMLInputElement).value.trim();
    // Debounced: pageIndex reset + reload happen once typing settles (see ngOnInit).
    this.filterChange$.next();
  }

  /** Distinguishes "this query returned nothing" from "your filters excluded everything". */
  hasColumnFilters(): boolean {
    return Object.values(this.columnFilters).some(v => !!v);
  }

  /**
   * Downloads the complete result set in the chosen format (Excel/CSV/PDF/JSON), reusing the
   * result cached during execution — no second query run. Includes every matching row, not
   * just the current page.
   */
  exportResults(format: ExportFormat): void {
    if (!this.result || !this.query || !this.resultJobId) return;

    this.exporting = true;
    this.exportSub = this.queryService.exportJob(this.resultJobId, format).subscribe({
      next: (blob) => {
        this.exporting = false;
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${this.query?.name || 'results'}_${new Date().toISOString().slice(0, 10)}.${format}`;
        a.click();
        window.URL.revokeObjectURL(url);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.exporting = false;
        this.toast.error(err, 'user.execute.exportFailed', 6000);
        this.cdr.detectChanges();
      }
    });
  }
}
