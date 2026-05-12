import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, of, throwError } from 'rxjs';
import { catchError, timeout } from 'rxjs/operators';
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
                <!-- String -->
                <mat-form-field *ngIf="param.parameterType === 0" appearance="outline">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput [formControlName]="param.name">
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

                <!-- Dropdown -->
                <mat-form-field *ngIf="param.parameterType === 4" appearance="outline">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <mat-select [formControlName]="param.name">
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
                {{ executing ? 'Executing...' : 'Execute Query' }}
              </button>
              <button mat-stroked-button type="button" *ngIf="result && result.columns.length > 0"
                      (click)="exportCsv()">
                <mat-icon>download</mat-icon> Export CSV
              </button>
            </div>
          </form>
        </mat-card-content>
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
    .col-filter-input:focus { border-color: var(--primary, #1976d2); box-shadow: 0 0 0 2px rgba(25,118,210,.15); }
    .col-filter-input::placeholder { color: var(--text-hint, #999); }
    .non-query-result { display: flex; align-items: center; gap: 8px; padding: 24px 0; color: var(--status-success); }
    .non-query-result mat-icon { font-size: 32px; width: 32px; height: 32px; }
    .non-query-result p { font-size: 16px; margin: 0; }
    .loading-hint { margin-bottom: 16px; }
    .loading-hint p { margin-top: 8px; font-size: 13px; color: var(--text-secondary); }
  `]
})
export class QueryExecuteComponent implements OnInit {
  query?: DynamicQuery;
  form!: FormGroup;
  result?: QueryExecutionResult;
  dataSource = new MatTableDataSource<Record<string, any>>();
  executing = false;
  loadingQuery = true;
  loadingDropdowns = false;
  queryError = '';
  columnFilters: Record<string, string> = {};
  filterColumns: string[] = [];

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
          const validators: any[] = param.isRequired ? [Validators.required] : [];
          if (param.parameterType === ParameterType.String) {
            validators.push(this.sqlInjectionValidator);
          }
          let defaultValue: any = param.defaultValue || '';

          if (param.parameterType === ParameterType.Boolean) {
            defaultValue = param.defaultValue === 'true';
          }

          this.form.addControl(param.name, this.fb.control(defaultValue, validators));
        }

        // Load options for all dropdown parameters in parallel
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

    this.executing = true;
    const params: Record<string, string> = {};

    for (const param of this.query.parameters) {
      let value = this.form.get(param.name)?.value;

      if (param.parameterType === ParameterType.Date && value instanceof Date) {
        value = value.toISOString().split('T')[0];
      } else if (param.parameterType === ParameterType.Boolean) {
        value = String(value);
      } else {
        value = String(value ?? '');
      }

      params[param.name] = value;
    }

    this.queryService.executeQuery(this.query.id, params).subscribe({
      next: (res) => {
        this.result = res;
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
        this.executing = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.executing = false;
        this.snackBar.open(
          err.error?.message || 'Query execution failed',
          'Close', { duration: 5000 }
        );
        this.cdr.detectChanges();
      }
    });
  }

  applyColumnFilter(event: Event, col: string): void {
    const value = (event.target as HTMLInputElement).value.trim();
    this.columnFilters[col] = value;
    this.dataSource.filter = JSON.stringify(this.columnFilters);
    if (this.dataSource.paginator) {
      this.dataSource.paginator.firstPage();
    }
  }

  exportCsv(): void {
    if (!this.result) return;

    const escape = (val: string) => {
      if (val.includes('"') || val.includes(',') || val.includes('\n') || val.includes('\r')) {
        return `"${val.replace(/"/g, '""')}"`;
      }
      return val;
    };

    const headers = this.result.columns.map(escape).join(',');
    const rows = this.result.rows.map(row =>
      this.result!.columns.map(col => escape(String(row[col] ?? ''))).join(',')
    );

    const csv = [headers, ...rows].join('\r\n');
    const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${this.query?.name || 'results'}_${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    window.URL.revokeObjectURL(url);
  }
}
