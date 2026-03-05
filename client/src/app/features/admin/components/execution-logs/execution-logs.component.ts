import { Component, OnInit, ViewChild, ViewChildren, QueryList } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { QueryService } from '@core/services/query.service';
import { ExecutionLog, ParameterChangeHistory } from '@core/models/dynamic-query.model';

@Component({
  selector: 'app-execution-logs',
  template: `
    <div class="container">
      <h2>Execution Logs</h2>

      <mat-tab-group>
        <mat-tab label="Execution Logs">
          <mat-card>
            <mat-card-content>
              <div *ngIf="loading" class="loading">
                <mat-spinner diameter="40"></mat-spinner>
              </div>

              <table mat-table [dataSource]="dataSource" matSort *ngIf="!loading">
                <ng-container matColumnDef="queryName">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>Query</th>
                  <td mat-cell *matCellDef="let log">{{ log.queryName }}</td>
                </ng-container>

                <ng-container matColumnDef="username">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>User</th>
                  <td mat-cell *matCellDef="let log">{{ log.username }}</td>
                </ng-container>

                <ng-container matColumnDef="executedAt">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>Executed At</th>
                  <td mat-cell *matCellDef="let log">{{ log.executedAt | date:'medium' }}</td>
                </ng-container>

                <ng-container matColumnDef="executionDurationMs">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>Duration (ms)</th>
                  <td mat-cell *matCellDef="let log">{{ log.executionDurationMs }}</td>
                </ng-container>

                <ng-container matColumnDef="rowsReturned">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>Rows</th>
                  <td mat-cell *matCellDef="let log">{{ log.rowsReturned }}</td>
                </ng-container>

                <ng-container matColumnDef="isSuccess">
                  <th mat-header-cell *matHeaderCellDef mat-sort-header>Status</th>
                  <td mat-cell *matCellDef="let log">
                    <mat-icon [class]="log.isSuccess ? 'success' : 'error'">
                      {{ log.isSuccess ? 'check_circle' : 'error' }}
                    </mat-icon>
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
              </table>

              <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons>
              </mat-paginator>
            </mat-card-content>
          </mat-card>
        </mat-tab>

        <mat-tab label="Parameter Changes">
          <mat-card>
            <mat-card-content>
              <div *ngIf="historyLoading" class="loading">
                <mat-spinner diameter="40"></mat-spinner>
              </div>

              <table mat-table [dataSource]="historyDataSource" *ngIf="!historyLoading && historyDataSource.data.length > 0">
                <ng-container matColumnDef="queryName">
                  <th mat-header-cell *matHeaderCellDef>Query</th>
                  <td mat-cell *matCellDef="let row">{{ row.queryName }}</td>
                </ng-container>

                <ng-container matColumnDef="parameterName">
                  <th mat-header-cell *matHeaderCellDef>Parameter</th>
                  <td mat-cell *matCellDef="let row">{{ row.parameterName }}</td>
                </ng-container>

                <ng-container matColumnDef="changeType">
                  <th mat-header-cell *matHeaderCellDef>Change</th>
                  <td mat-cell *matCellDef="let row">
                    <span class="change-badge" [ngClass]="row.changeType.toLowerCase()">
                      {{ row.changeType }}
                    </span>
                  </td>
                </ng-container>

                <ng-container matColumnDef="fieldName">
                  <th mat-header-cell *matHeaderCellDef>Field</th>
                  <td mat-cell *matCellDef="let row">{{ row.fieldName }}</td>
                </ng-container>

                <ng-container matColumnDef="newValue">
                  <th mat-header-cell *matHeaderCellDef>New Value</th>
                  <td mat-cell *matCellDef="let row">
                    <span class="value-cell new-value" [matTooltip]="row.newValue || '(empty)'">
                      {{ row.newValue || '(empty)' }}
                    </span>
                  </td>
                </ng-container>

                <ng-container matColumnDef="changedAt">
                  <th mat-header-cell *matHeaderCellDef>Changed At</th>
                  <td mat-cell *matCellDef="let row">{{ row.changedAt | date:'medium' }}</td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="historyColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: historyColumns;"></tr>
              </table>

              <div *ngIf="!historyLoading && historyDataSource.data.length === 0" class="no-data">
                No parameter changes recorded yet.
              </div>

              <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons>
              </mat-paginator>
            </mat-card-content>
          </mat-card>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .loading { display: flex; justify-content: center; padding: 40px; }
    .success { color: #4caf50; }
    .error { color: #f44336; }
    table { width: 100%; }
    .change-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }
    .change-badge.added { background: #e8f5e9; color: #2e7d32; }
    .change-badge.modified { background: #fff3e0; color: #e65100; }
    .change-badge.removed { background: #ffebee; color: #c62828; }
    .value-cell {
      max-width: 200px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      display: block;
      font-size: 12px;
      cursor: default;
    }
    .new-value { color: #2e7d32; }
    .no-data {
      text-align: center;
      padding: 40px;
      color: #666;
      font-style: italic;
    }
    mat-card { margin-top: 8px; }
  `]
})
export class ExecutionLogsComponent implements OnInit {
  displayedColumns = ['queryName', 'username', 'executedAt', 'executionDurationMs', 'rowsReturned', 'isSuccess'];
  historyColumns = ['queryName', 'parameterName', 'changeType', 'fieldName', 'newValue', 'changedAt'];
  dataSource = new MatTableDataSource<ExecutionLog>();
  historyDataSource = new MatTableDataSource<ParameterChangeHistory>();
  loading = true;
  historyLoading = true;

  @ViewChildren(MatPaginator) paginators!: QueryList<MatPaginator>;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(private queryService: QueryService) {}

  ngOnInit(): void {
    this.queryService.getExecutionLogs().subscribe({
      next: (logs) => {
        this.dataSource.data = logs;
        this.dataSource.sort = this.sort;
        this.loading = false;
        setTimeout(() => {
          const p = this.paginators.toArray();
          if (p[0]) this.dataSource.paginator = p[0];
        });
      },
      error: () => this.loading = false
    });

    this.queryService.getParameterChangeHistory().subscribe({
      next: (history) => {
        this.historyDataSource.data = history;
        this.historyLoading = false;
        setTimeout(() => {
          const p = this.paginators.toArray();
          if (p[1]) this.historyDataSource.paginator = p[1];
        });
      },
      error: () => this.historyLoading = false
    });
  }

}

