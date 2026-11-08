import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoModule } from '@jsverse/transloco';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import {
  ExecutionLog,
  isWriteQueryType,
  QUERY_TYPE_LABELS,
  QueryType
} from '@core/models/dynamic-query.model';
import { LogParameter, parseLogParameters } from './log-parameters';
import { OldRowsFetch, OldRowsTableComponent } from './old-rows-table.component';

export interface ExecutionLogDetailData {
  log: ExecutionLog;
  /** Fetches the before-change rows; omitted when the log has none. */
  fetchOldRows?: OldRowsFetch;
  /** The admin log shows who ran it; a user's own history does not need to. */
  showUser: boolean;
}

/**
 * Everything recorded about one query run: who, when, how long, how many rows, the error if it
 * failed, every parameter it was given (multi-select values unpacked one per chip), and — for
 * an UPDATE or DELETE that kept a snapshot — the rows as they were before the change.
 */
@Component({
  standalone: true,
  selector: 'app-execution-log-detail-dialog',
  imports: [
    CommonModule,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    OldRowsTableComponent,
    TranslocoModule],
  template: `
    <h2 mat-dialog-title>{{ log.queryName }}</h2>
    <mat-dialog-content>
      <div class="badges">
        <span class="type-chip" [class.type-write]="isWrite">{{ typeLabel }}</span>
        <span class="status" [class.failed]="!log.isSuccess">
          <mat-icon aria-hidden="true">{{ log.isSuccess ? 'check_circle' : 'error' }}</mat-icon>
          {{ (log.isSuccess ? 'admin.logs.statusSuccess' : 'admin.logs.statusFailed') | transloco }}
        </span>
      </div>

      <dl class="facts">
        <div *ngIf="data.showUser">
          <dt>{{ 'admin.logs.user' | transloco }}</dt>
          <dd>{{ log.username }}</dd>
        </div>
        <div>
          <dt>{{ 'admin.logs.executedAt' | transloco }}</dt>
          <dd>{{ log.executedAt | date:'medium' }}</dd>
        </div>
        <div>
          <dt>{{ 'admin.logs.duration' | transloco }}</dt>
          <dd>{{ 'admin.logs.durationValue' | transloco: { ms: (log.executionDurationMs | number) } }}</dd>
        </div>
        <div>
          <dt>{{ (isWrite ? 'admin.logs.rowsAffected' : 'admin.logs.rowsReturned') | transloco }}</dt>
          <dd>{{ log.rowsReturned | number }}</dd>
        </div>
      </dl>

      <section *ngIf="!log.isSuccess" class="error-panel" role="alert">
        <h3>{{ 'admin.logs.errorTitle' | transloco }}</h3>
        <p>{{ log.errorMessage || ('common.unknownError' | transloco) }}</p>
      </section>

      <section>
        <h3>{{ 'admin.logs.parametersUsed' | transloco }}</h3>
        <p *ngIf="parameters.length === 0" class="muted">{{ 'admin.logs.noParametersUsed' | transloco }}</p>
        <table *ngIf="parameters.length > 0" class="params-table">
          <thead>
            <tr>
              <th scope="col">{{ 'admin.logs.paramName' | transloco }}</th>
              <th scope="col">{{ 'admin.logs.paramValue' | transloco }}</th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let p of parameters">
              <th scope="row" class="param-name">{{ p.name }}</th>
              <td>
                <span *ngIf="p.isEmpty" class="muted empty">{{ 'admin.logs.emptyValue' | transloco }}</span>
                <span *ngIf="!p.isEmpty && p.values.length === 1" class="param-value">{{ p.values[0] }}</span>
                <span *ngIf="!p.isEmpty && p.values.length > 1" class="value-list">
                  <span *ngFor="let v of p.values" class="value-chip">{{ v }}</span>
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <section *ngIf="data.fetchOldRows && log.hasOldValues">
        <h3>{{ 'admin.logs.rowsBefore' | transloco }}</h3>
        <app-old-rows-table [fetch]="data.fetchOldRows" maxHeight="40vh"></app-old-rows-table>
      </section>

      <p *ngIf="showNoSnapshotNote" class="muted note">
        <mat-icon aria-hidden="true">info</mat-icon>
        {{ 'admin.logs.noSnapshot' | transloco }}
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-stroked-button (click)="close()">{{ 'common.close' | transloco }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .badges { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
    .status {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-size: 13px;
      font-weight: 500;
      color: var(--status-success);
    }
    .status.failed { color: var(--status-error); }
    .status mat-icon { font-size: 18px; width: 18px; height: 18px; }

    .facts {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
      gap: 12px 24px;
      margin: 0 0 20px;
    }
    .facts dt { font-size: 12px; color: var(--text-secondary); margin-bottom: 2px; }
    .facts dd { margin: 0; font-size: 14px; color: var(--text-primary); }

    section { margin-bottom: 20px; }
    h3 { font-size: 14px; font-weight: 600; margin: 0 0 8px; color: var(--text-primary); }

    .error-panel {
      padding: 12px 16px;
      border-radius: 6px;
      background: var(--toast-error-bg);
      color: var(--toast-error-fg);
    }
    .error-panel h3 { color: inherit; }
    .error-panel p { margin: 0; white-space: pre-wrap; word-break: break-word; font-size: 13px; }

    .params-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
      border: 1px solid var(--border-color);
      border-radius: 4px;
    }
    .params-table th, .params-table td {
      padding: 8px 12px;
      text-align: start;
      vertical-align: top;
      border-bottom: 1px solid var(--divider-color);
    }
    .params-table thead th {
      font-weight: 600;
      color: var(--text-secondary);
      background: var(--bg-secondary);
    }
    .params-table tbody tr:last-child th,
    .params-table tbody tr:last-child td { border-bottom: none; }
    .param-name { width: 35%; color: var(--sql-param); font-weight: 600; word-break: break-word; }
    .param-value { white-space: pre-wrap; word-break: break-word; color: var(--text-primary); }
    .value-list { display: flex; flex-wrap: wrap; gap: 4px; }
    .value-chip {
      padding: 1px 8px;
      border-radius: 10px;
      background: var(--chip-accent);
      color: var(--chip-text);
      font-size: 12px;
    }

    .muted { color: var(--text-secondary); font-size: 13px; margin: 0; }
    .empty { font-style: italic; }
    .note { display: flex; align-items: flex-start; gap: 6px; }
    .note mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
  `]
})
export class ExecutionLogDetailDialogComponent {
  readonly dialogRef = inject(MatDialogRef<ExecutionLogDetailDialogComponent>);
  readonly data = inject<ExecutionLogDetailData>(MAT_DIALOG_DATA);

  readonly log: ExecutionLog = this.data.log;
  readonly parameters: LogParameter[] = parseLogParameters(this.log.parameters);
  readonly isWrite = isWriteQueryType(this.log.queryType);
  readonly typeLabel = QUERY_TYPE_LABELS[this.log.queryType] ?? QUERY_TYPE_LABELS[QueryType.Other];

  /**
   * An UPDATE or DELETE that ran but kept no snapshot — say why the before-rows are missing
   * rather than leave the reader wondering. An INSERT has no "before", so it gets no note.
   */
  readonly showNoSnapshotNote =
    this.log.isSuccess && !this.log.hasOldValues &&
    (this.log.queryType === QueryType.Update || this.log.queryType === QueryType.Delete);

  close(): void {
    this.dialogRef.close();
  }
}
