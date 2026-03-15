import { Component, OnInit, ViewChild, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatPaginator } from '@angular/material/paginator';
import { MatTableDataSource } from '@angular/material/table';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin, of, throwError } from 'rxjs';
import { catchError, timeout } from 'rxjs/operators';
import { QueryService } from '@core/services/query.service';
import {
  DatabaseUserSummary,
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

            <!-- Database User Selection -->
            <mat-form-field *ngIf="availableDbUsers.length > 0" appearance="outline" class="db-user-select">
              <mat-label>Database User</mat-label>
              <mat-select [(value)]="selectedDbUserId">
                <mat-option [value]="null">
                  {{ query?.databaseUserName ? query.databaseUserName + ' (default)' : 'Default connection' }}
                </mat-option>
                <mat-option *ngFor="let du of availableDbUsers" [value]="du.id">
                  {{ du.name }}
                </mat-option>
              </mat-select>
              <mat-hint>Override the database user for this execution</mat-hint>
            </mat-form-field>

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
            <table mat-table [dataSource]="dataSource">
              <ng-container *ngFor="let col of result.columns" [matColumnDef]="col">
                <th mat-header-cell *matHeaderCellDef>{{ col }}</th>
                <td mat-cell *matCellDef="let row">{{ row[col] }}</td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="result.columns"></tr>
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
    .non-query-result { display: flex; align-items: center; gap: 8px; padding: 24px 0; color: var(--status-success); }
    .non-query-result mat-icon { font-size: 32px; width: 32px; height: 32px; }
    .non-query-result p { font-size: 16px; margin: 0; }
    .loading-hint { margin-bottom: 16px; }
    .loading-hint p { margin-top: 8px; font-size: 13px; color: var(--text-secondary); }
    .db-user-select { width: 100%; margin-top: 8px; }
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

  /** Maps param.name -> list of dropdown options */
  dropdownOptions: Record<string, DropdownOption[]> = {};
  availableDbUsers: DatabaseUserSummary[] = [];
  selectedDbUserId: string | null = null;

  @ViewChild(MatPaginator) paginator!: MatPaginator;

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
    this.queryService.getAccessibleDatabaseUsers().subscribe({
      next: (users) => { this.availableDbUsers = users; this.cdr.detectChanges(); },
      error: () => {}
    });
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
          const validators = param.isRequired ? [Validators.required] : [];
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

    this.queryService.executeQuery(this.query.id, params, this.selectedDbUserId).subscribe({
      next: (res) => {
        this.result = res;
        this.dataSource.data = res.rows;
        setTimeout(() => {
          if (this.paginator) {
            this.dataSource.paginator = this.paginator;
          }
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

  exportCsv(): void {
    if (!this.result) return;

    const headers = this.result.columns.join(',');
    const rows = this.result.rows.map(row =>
      this.result!.columns.map(col => {
        const val = String(row[col] ?? '');
        return val.includes(',') ? `"${val}"` : val;
      }).join(',')
    );

    const csv = [headers, ...rows].join('\n');
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${this.query?.name || 'results'}_${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    window.URL.revokeObjectURL(url);
  }
}
