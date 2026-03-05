import { Component, OnInit, ViewChild } from '@angular/core';
import { MatPaginator } from '@angular/material/paginator';
import { MatSort } from '@angular/material/sort';
import { MatTableDataSource } from '@angular/material/table';
import { QueryService } from '@core/services/query.service';
import { ParameterChangeHistory } from '@core/models/dynamic-query.model';

@Component({
  selector: 'app-parameter-change-history',
  template: `
    <div class="container">
      <h2>Parameter Change History</h2>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <table mat-table [dataSource]="dataSource" matSort *ngIf="!loading">
            <ng-container matColumnDef="queryName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Query</th>
              <td mat-cell *matCellDef="let row">{{ row.queryName }}</td>
            </ng-container>

            <ng-container matColumnDef="parameterName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Parameter</th>
              <td mat-cell *matCellDef="let row">{{ row.parameterName }}</td>
            </ng-container>

            <ng-container matColumnDef="changeType">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Change</th>
              <td mat-cell *matCellDef="let row">
                <span class="change-badge" [ngClass]="row.changeType.toLowerCase()">
                  {{ row.changeType }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="fieldName">
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Field</th>
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
              <th mat-header-cell *matHeaderCellDef mat-sort-header>Changed At</th>
              <td mat-cell *matCellDef="let row">{{ row.changedAt | date:'medium' }}</td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
          </table>

          <div *ngIf="!loading && dataSource.data.length === 0" class="no-data">
            No parameter changes recorded yet.
          </div>

          <mat-paginator [pageSizeOptions]="[10, 25, 50]" showFirstLastButtons>
          </mat-paginator>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .loading { display: flex; justify-content: center; padding: 40px; }
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
  `]
})
export class ParameterChangeHistoryComponent implements OnInit {
  displayedColumns = ['queryName', 'parameterName', 'changeType', 'fieldName', 'newValue', 'changedAt'];
  dataSource = new MatTableDataSource<ParameterChangeHistory>();
  loading = true;

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(MatSort) sort!: MatSort;

  constructor(private queryService: QueryService) {}

  ngOnInit(): void {
    this.queryService.getParameterChangeHistory().subscribe({
      next: (history) => {
        this.dataSource.data = history;
        this.dataSource.paginator = this.paginator;
        this.dataSource.sort = this.sort;
        this.loading = false;
      },
      error: () => this.loading = false
    });
  }
}
