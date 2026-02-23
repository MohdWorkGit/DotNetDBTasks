import { Component, OnInit, ViewChild } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatPaginator } from '@angular/material/paginator';
import { MatTableDataSource } from '@angular/material/table';
import { MatSnackBar } from '@angular/material/snack-bar';
import { QueryService } from '@core/services/query.service';
import { DynamicQuery, ParameterType, QueryExecutionResult } from '@core/models/dynamic-query.model';

@Component({
  selector: 'app-query-execute',
  template: `
    <div class="container">
      <h2 *ngIf="query">{{ query.name }}</h2>
      <p *ngIf="query" class="description">{{ query.description }}</p>

      <mat-card *ngIf="query">
        <mat-card-header>
          <mat-card-title>Parameters</mat-card-title>
        </mat-card-header>
        <mat-card-content>
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
              </ng-container>
            </div>

            <div class="actions">
              <button mat-raised-button color="primary" type="submit"
                      [disabled]="form.invalid || executing">
                <mat-icon>play_arrow</mat-icon>
                {{ executing ? 'Executing...' : 'Execute Query' }}
              </button>
              <button mat-stroked-button type="button" *ngIf="result && result.affectedRows == null"
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
          <mat-card-subtitle *ngIf="result.affectedRows == null">
            {{ result.totalRows }} rows returned in {{ result.executionDurationMs }}ms
          </mat-card-subtitle>
          <mat-card-subtitle *ngIf="result.affectedRows != null">
            {{ result.affectedRows }} rows affected in {{ result.executionDurationMs }}ms
          </mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <div *ngIf="result.affectedRows != null" class="affected-rows-message">
            <mat-icon color="primary">check_circle</mat-icon>
            <span>Query executed successfully. {{ result.affectedRows }} row(s) affected.</span>
          </div>

          <div *ngIf="result.affectedRows == null" class="table-wrapper">
            <table mat-table [dataSource]="dataSource">
              <ng-container *ngFor="let col of result.columns" [matColumnDef]="col">
                <th mat-header-cell *matHeaderCellDef>{{ col }}</th>
                <td mat-cell *matCellDef="let row">{{ row[col] }}</td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="result.columns"></tr>
              <tr mat-row *matRowDef="let row; columns: result.columns;"></tr>
            </table>
          </div>

          <mat-paginator *ngIf="result.affectedRows == null" [pageSizeOptions]="[10, 25, 50, 100]" showFirstLastButtons>
          </mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .description { color: #666; margin-bottom: 16px; }
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
    .affected-rows-message { display: flex; align-items: center; gap: 8px; padding: 16px 0; font-size: 16px; }
  `]
})
export class QueryExecuteComponent implements OnInit {
  query?: DynamicQuery;
  form!: FormGroup;
  result?: QueryExecutionResult;
  dataSource = new MatTableDataSource<Record<string, any>>();
  executing = false;

  @ViewChild(MatPaginator) paginator!: MatPaginator;

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({});
    const queryId = this.route.snapshot.params['id'];

    this.queryService.getMyQueries().subscribe(queries => {
      this.query = queries.find(q => q.id === queryId);
      if (!this.query) return;

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

    this.queryService.executeQuery(this.query.id, params).subscribe({
      next: (res) => {
        this.result = res;
        this.dataSource.data = res.rows;
        setTimeout(() => {
          if (this.paginator) {
            this.dataSource.paginator = this.paginator;
          }
        });
        this.executing = false;
      },
      error: (err) => {
        this.executing = false;
        this.snackBar.open(
          err.error?.message || 'Query execution failed',
          'Close', { duration: 5000 }
        );
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
