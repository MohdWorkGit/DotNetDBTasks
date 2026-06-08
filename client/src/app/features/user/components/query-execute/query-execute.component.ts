import { Component, OnInit, OnDestroy, ViewChild, ChangeDetectorRef } from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, of, throwError, Subscription } from 'rxjs';
import { catchError, switchMap, timeout } from 'rxjs/operators';
import { QueryService } from '@core/services/query.service';
import {
  DropdownOption,
  DynamicQuery,
  ParameterType,
  QueryExecutionResult
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
            <mat-icon>refresh</mat-icon> Retry
          </button>
        </mat-card-content>
      </mat-card>

      <h2 *ngIf="query">{{ query.name }}</h2>
      <p *ngIf="query" class="description">{{ query.description }}</p>

      <mat-card *ngIf="query">
        <mat-card-header>
          <mat-card-title>Parameters</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <div *ngIf="loadingDropdowns" class="loading-hint">
            <mat-progress-bar mode="indeterminate"></mat-progress-bar>
            <p>Loading dropdown options...</p>
          </div>

          <form [formGroup]="form" (ngSubmit)="execute()">
            <div class="form-grid">
              <ng-container *ngFor="let param of query.parameters">
                <!-- String (single value, or comma-separated when allowMultiple) -->
                <mat-form-field *ngIf="param.parameterType === 0" appearance="outline">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput [formControlName]="param.name"
                         [placeholder]="param.allowMultiple ? 'value1, value2, value3' : ''">
                  <mat-hint *ngIf="param.allowMultiple">
                    Enter multiple values separated by commas
                  </mat-hint>
                  <mat-error *ngIf="form.get(param.name)?.hasError('required')">
                    {{ param.displayName }} is required
                  </mat-error>
                  <mat-error *ngIf="form.get(param.name)?.hasError('sqlInjection')">
                    Invalid input: SQL keywords or special characters are not allowed
                  </mat-error>
                </mat-form-field>

                <!-- Number -->
                <mat-form-field *ngIf="param.parameterType === 1" appearance="outline">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput type="number" [formControlName]="param.name">
                  <mat-error *ngIf="form.get(param.name)?.hasError('required')">
                    {{ param.displayName }} is required
                  </mat-error>
                </mat-form-field>

                <!-- Date -->
                <mat-form-field *ngIf="param.parameterType === 2" appearance="outline">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput [matDatepicker]="picker" [formControlName]="param.name">
                  <mat-datepicker-toggle matIconSuffix [for]="picker"></mat-datepicker-toggle>
                  <mat-datepicker #picker></mat-datepicker>
                  <mat-error *ngIf="form.get(param.name)?.hasError('required')">
                    {{ param.displayName }} is required
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
                    No options available
                  </mat-hint>
                  <mat-error *ngIf="form.get(param.name)?.hasError('required')">
                    {{ param.displayName }} is required
                  </mat-error>
                </mat-form-field>
              </ng-container>
            </div>

            <div class="actions">
              <button mat-raised-button color="primary" type="submit"
                      [disabled]="form.invalid || executing || loadingDropdowns">
                <mat-icon>play_arrow</mat-icon>
                {{ executing
                    ? (query.isLongRunning ? 'Executing… ' + formatElapsed(elapsedSeconds) : 'Executing…')
                    : 'Execute Query' }}
              </button>
              <button mat-stroked-button color="warn" type="button"
                      *ngIf="executing && query.isLongRunning" (click)="cancelExecution()">
                <mat-icon>cancel</mat-icon> Cancel
              </button>
              <button mat-stroked-button type="button" *ngIf="result && result.columns.length > 0"
                      (click)="exportExcel()" [disabled]="exporting">
                <mat-icon>download</mat-icon>
                {{ exporting ? 'Exporting…' : 'Export Excel' }}
              </button>
              <button mat-stroked-button color="warn" type="button"
                      *ngIf="exporting" (click)="cancelExport()">
                <mat-icon>cancel</mat-icon> Cancel Export
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>

      <mat-card *ngIf="pendingPreview" class="confirm-card">
        <mat-card-header>
          <mat-icon mat-card-avatar class="warn-icon">warning</mat-icon>
          <mat-card-title>Confirm changes</mat-card-title>
          <mat-card-subtitle>This query will modify data. Review before committing.</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <p>
            <strong>{{ pendingPreview.affectedRows }}</strong>
            {{ pendingPreview.affectedRows === 1 ? 'row' : 'rows' }} will be affected.
            The change has not been committed yet.
          </p>
          <div *ngIf="pendingPreview.previewRows && pendingPreview.previewRows.length > 0"
               class="preview-table-wrapper">
            <p class="preview-table-title">Rows that will be affected:</p>
            <table mat-table [dataSource]="pendingPreview.previewRows" class="preview-table">
              <ng-container *ngFor="let col of pendingPreview.previewColumns || []" [matColumnDef]="col">
                <th mat-header-cell *matHeaderCellDef>{{ col }}</th>
                <td mat-cell *matCellDef="let row">{{ row[col] }}</td>
              </ng-container>
              <tr mat-header-row *matHeaderRowDef="pendingPreview.previewColumns || []"></tr>
              <tr mat-row *matRowDef="let row; columns: pendingPreview.previewColumns || [];"></tr>
            </table>
          </div>
        </mat-card-content>
        <mat-card-actions align="end">
          <button mat-stroked-button type="button" (click)="cancelConfirm()" [disabled]="executing">
            Cancel
          </button>
          <button mat-raised-button color="warn" type="button" (click)="confirmExecute()" [disabled]="executing">
            <mat-icon>check</mat-icon>
            {{ executing ? 'Committing...' : 'Confirm & Commit' }}
          </button>
        </mat-card-actions>
      </mat-card>

      <mat-card *ngIf="result" class="results-card">
        <mat-card-header>
          <mat-card-title>Results</mat-card-title>
          <mat-card-subtitle>
            <span *ngIf="result.columns.length > 0">
              {{ result.totalRows }} rows returned in {{ result.executionDurationMs }}ms
            </span>
            <span *ngIf="result.columns.length === 0">
              {{ result.affectedRows }} rows affected in {{ result.executionDurationMs }}ms
            </span>
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div *ngIf="result.isLimitReached" class="limit-warning">
            <mat-icon>warning_amber</mat-icon>
            <span>
              Only the first {{ result.totalRows }} rows are shown (display limit reached).
              Use <strong>Export Excel</strong> to download the complete result set.
            </span>
          </div>

          <div *ngIf="result.columns.length > 0" class="table-wrapper">
            <table mat-table [dataSource]="dataSource" matSort>
              <ng-container *ngFor="let col of result.columns" [matColumnDef]="col">
                <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ col }}</th>
                <td mat-cell *matCellDef="let row">{{ row[col] }}</td>
              </ng-container>

              <ng-container *ngFor="let col of result.columns" [matColumnDef]="'filter_' + col">
                <th mat-header-cell *matHeaderCellDef>
                  <input class="col-filter-input"
                         [value]="columnFilters[col] || ''"
                         placeholder="Filter..."
                         (input)="applyColumnFilter($event, col)" />
                </th>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="result.columns"></tr>
              <tr mat-header-row *matHeaderRowDef="filterColumns" class="filter-row"></tr>
              <tr mat-row *matRowDef="let row; columns: result.columns;"></tr>
            </table>
          </div>

          <div *ngIf="result.columns.length === 0" class="non-query-result">
            <mat-icon>check_circle</mat-icon>
            <p>Query executed successfully. {{ result.affectedRows }} rows affected.</p>
          </div>

          <mat-paginator *ngIf="result.columns.length > 0" [pageSizeOptions]="[10, 25, 50, 100]" showFirstLastButtons>
          </mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .loading { display: flex; justify-content: center; padding: 40px; }
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
    .table-wrapper { overflow-x: auto; }
    table { width: 100%; }
    .filter-row th { padding-top: 4px; padding-bottom: 4px; background: var(--bg-secondary, #f5f5f5); }
    .col-filter-input {
      width: 100%;
      box-sizing: border-box;
      border: 1px solid var(--border-color, #ccc);
      border-radius: 4px;
      padding: 4px 6px;
      font-size: 12px;
      background: var(--bg-primary, #fff);
      color: inherit;
      outline: none;
    }
    .col-filter-input:focus { border-color: var(--accent-primary); box-shadow: 0 0 0 2px rgba(99,102,241,.25); }
    .col-filter-input::placeholder { color: var(--text-hint, #999); }
    .limit-warning {
      display: flex; align-items: center; gap: 8px;
      padding: 10px 14px; margin-bottom: 12px;
      border-radius: 4px;
      background: var(--status-warning-bg, #fff8e1);
      border: 1px solid var(--status-warning, #f59e0b);
      color: var(--status-warning-text, #92400e);
      font-size: 13px;
    }
    .limit-warning mat-icon { color: var(--status-warning, #f59e0b); flex-shrink: 0; }
    .non-query-result { display: flex; align-items: center; gap: 8px; padding: 24px 0; color: var(--status-success); }
    .non-query-result mat-icon { font-size: 32px; width: 32px; height: 32px; }
    .non-query-result p { font-size: 16px; margin: 0; }
    .loading-hint { margin-bottom: 16px; }
    .loading-hint p { margin-top: 8px; font-size: 13px; color: var(--text-secondary); }
    .confirm-card { margin-top: 16px; border-left: 4px solid var(--status-warning, #f59e0b); }
    .confirm-card .warn-icon {
      display: flex; align-items: center; justify-content: center;
      background: var(--status-warning, #f59e0b); color: #fff; border-radius: 50%;
    }
    .confirm-card p { font-size: 15px; margin: 0; }
    .preview-table-wrapper { margin-top: 12px; overflow-x: auto; max-height: 320px; overflow-y: auto; border: 1px solid var(--border-color, #e0e0e0); border-radius: 4px; }
    .preview-table-title { font-size: 13px; margin: 0 0 8px 0; color: var(--text-secondary); }
    .preview-table { width: 100%; font-size: 13px; }
    .preview-table th { font-weight: 600; background: var(--bg-secondary, #fafafa); position: sticky; top: 0; z-index: 1; }
  `]
})
export class QueryExecuteComponent implements OnInit, OnDestroy {
  query?: DynamicQuery;
  form!: FormGroup;
  result?: QueryExecutionResult;
  /** Preview returned by the backend for a write query awaiting user confirmation. */
  pendingPreview?: QueryExecutionResult;
  /** Parameters used for the pending preview, replayed on confirm. */
  private pendingParams: Record<string, string> = {};
  dataSource = new MatTableDataSource<Record<string, any>>();
  executing = false;
  exporting = false;
  /** Parameters that produced the currently displayed result, replayed for the Excel export. */
  private lastResultParams: Record<string, string> = {};
  loadingQuery = true;
  loadingDropdowns = false;
  queryError = '';
  columnFilters: Record<string, string> = {};
  filterColumns: string[] = [];

  /** Seconds elapsed since the current execution started, shown on the button. */
  elapsedSeconds = 0;
  /** Job id of the in-flight execution, used to cancel it server-side. */
  private currentJobId?: string;
  private currentExportJobId?: string;
  private pollSub?: Subscription;
  private exportSub?: Subscription;
  private timerHandle?: any;

  /** Maps param.name -> list of dropdown options */
  dropdownOptions: Record<string, DropdownOption[]> = {};

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  private queryId = '';

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({});
    this.queryId = this.route.snapshot.params['id'];
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
        this.queryError = err.error?.message || 'Failed to load query. Please try again.';
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

  execute(): void {
    if (this.form.invalid || !this.query) return;

    this.pendingPreview = undefined;
    this.runExecute(this.buildParams(), false);
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

  private handleResult(res: QueryExecutionResult, params: Record<string, string>, confirmed: boolean): void {
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
    this.lastResultParams = params;
    this.columnFilters = {};
    this.filterColumns = res.columns.map(c => 'filter_' + c);
    this.dataSource.filterPredicate = (row: Record<string, any>, filter: string) => {
      const filters: Record<string, string> = JSON.parse(filter || '{}');
      return Object.entries(filters).every(([col, val]) => {
        if (!val) return true;
        return String(row[col] ?? '').toLowerCase().includes(val.toLowerCase());
      });
    };
    this.dataSource.data = res.rows;
    this.dataSource.filter = '';
    setTimeout(() => {
      if (this.paginator) this.dataSource.paginator = this.paginator;
      if (this.sort) this.dataSource.sort = this.sort;
    });
    this.cdr.detectChanges();
  }

  private handleError(err: any): void {
    this.stopExecuting();
    this.snackBar.open(
      err.error?.message || 'Query execution failed',
      'Close', { duration: 5000 }
    );
    this.cdr.detectChanges();
  }

  /** Cancels the in-flight execution: stops polling and aborts the query server-side. */
  cancelExecution(): void {
    const jobId = this.currentJobId;
    this.pollSub?.unsubscribe();
    this.stopExecuting();
    if (jobId) {
      this.queryService.cancelJob(jobId).subscribe({
        next: () => this.snackBar.open('Query canceled', 'Close', { duration: 3000 }),
        error: () => this.snackBar.open('Query canceled', 'Close', { duration: 3000 })
      });
    }
    this.cdr.detectChanges();
  }

  private startTimer(): void {
    this.elapsedSeconds = 0;
    this.stopTimer();
    this.timerHandle = setInterval(() => {
      this.elapsedSeconds++;
      this.cdr.detectChanges();
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timerHandle) {
      clearInterval(this.timerHandle);
      this.timerHandle = undefined;
    }
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
    this.stopTimer();
  }

  applyColumnFilter(event: Event, col: string): void {
    const value = (event.target as HTMLInputElement).value.trim();
    this.columnFilters[col] = value;
    this.dataSource.filter = JSON.stringify(this.columnFilters);
    if (this.dataSource.paginator) {
      this.dataSource.paginator.firstPage();
    }
  }

  /**
   * Downloads the complete result set as an Excel (.xlsx) file. The export is generated
   * server-side by re-running the query with no row cap, so it includes every matching row —
   * not just the rows shown on screen.
   */
  exportExcel(): void {
    if (!this.result || !this.query) return;

    this.exporting = true;
    this.currentExportJobId = undefined;
    this.exportSub = this.queryService.exportQuery(
      this.query.id,
      this.lastResultParams,
      (jobId) => { this.currentExportJobId = jobId; }
    ).subscribe({
      next: (blob) => {
        this.exporting = false;
        this.currentExportJobId = undefined;
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${this.query?.name || 'results'}_${new Date().toISOString().slice(0, 10)}.xlsx`;
        a.click();
        window.URL.revokeObjectURL(url);
        this.cdr.detectChanges();
      },
      error: () => {
        this.exporting = false;
        this.currentExportJobId = undefined;
        this.snackBar.open('Export failed. Please try again.', 'Close', { duration: 5000 });
        this.cdr.detectChanges();
      }
    });
  }

  /** Cancels an in-progress export: stops polling and aborts the export job server-side. */
  cancelExport(): void {
    const jobId = this.currentExportJobId;
    this.exportSub?.unsubscribe();
    this.exporting = false;
    this.currentExportJobId = undefined;
    if (jobId) {
      this.queryService.cancelJob(jobId).subscribe({
        next: () => this.snackBar.open('Export canceled', 'Close', { duration: 3000 }),
        error: () => this.snackBar.open('Export canceled', 'Close', { duration: 3000 })
      });
    }
    this.cdr.detectChanges();
  }
}
