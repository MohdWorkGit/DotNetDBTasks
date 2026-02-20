import { Component, OnInit, ViewChild } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { QueryService } from '@core/services/query.service';
import { ExecutionLog } from '@core/models/dynamic-query.model';

@Component({
  selector: 'app-execution-logs',
  template: `
    <div class="container">
      <h2>Execution Logs</h2>

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
    </div>
  `,
  styles: [`
    .loading { display: flex; justify-content: center; padding: 40px; }
    .success { color: #4caf50; }
    .error { color: #f44336; }
    table { width: 100%; }
  `]
})
export class ExecutionLogsComponent implements OnInit {
  displayedColumns = ['queryName', 'username', 'executedAt', 'executionDurationMs', 'rowsReturned', 'isSuccess'];
  dataSource = new MatTableDataSource<ExecutionLog>();
  loading = true;

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(private queryService: QueryService) {}

  ngOnInit(): void {
    this.queryService.getExecutionLogs().subscribe({
      next: (logs) => {
        this.dataSource.data = logs;
        this.dataSource.paginator = this.paginator;
        this.dataSource.sort = this.sort;
        this.loading = false;
      },
      error: () => this.loading = false
    });
  }
}
