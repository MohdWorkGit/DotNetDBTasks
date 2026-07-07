import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ScheduledTaskService } from '@core/services/scheduled-task.service';
import { ScheduledTask, describeSchedule, utcDate } from '@core/models/scheduled-task.model';

@Component({
  standalone: false,
  selector: 'app-scheduled-tasks-list',
  template: `
    <div class="container">
      <div class="header">
        <h2>Scheduled Tasks</h2>
        <button mat-raised-button color="primary" routerLink="/admin/scheduled-tasks/create">
          <mat-icon>add_alarm</mat-icon> Create Task
        </button>
      </div>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <table mat-table [dataSource]="tasks" *ngIf="!loading">
            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef>Name</th>
              <td mat-cell *matCellDef="let t">
                <div class="task-name">{{ t.name }}</div>
                <div class="task-sub">{{ t.items.length }} quer{{ t.items.length === 1 ? 'y' : 'ies' }} → {{ t.outputFolder }}<span *ngIf="t.archiveFolder"> (+ {{ t.archiveFolder }})</span></div>
              </td>
            </ng-container>

            <ng-container matColumnDef="schedule">
              <th mat-header-cell *matHeaderCellDef>Schedule</th>
              <td mat-cell *matCellDef="let t">{{ describe(t) }}</td>
            </ng-container>

            <ng-container matColumnDef="enabled">
              <th mat-header-cell *matHeaderCellDef>Enabled</th>
              <td mat-cell *matCellDef="let t">
                <mat-icon [class.enabled]="t.isEnabled" [class.disabled]="!t.isEnabled">
                  {{ t.isEnabled ? 'check_circle' : 'pause_circle' }}
                </mat-icon>
              </td>
            </ng-container>

            <ng-container matColumnDef="nextRun">
              <th mat-header-cell *matHeaderCellDef>Next Run</th>
              <td mat-cell *matCellDef="let t">
                {{ t.isEnabled && t.nextRunAt ? (asDate(t.nextRunAt) | date:'medium') : '—' }}
              </td>
            </ng-container>

            <ng-container matColumnDef="lastRun">
              <th mat-header-cell *matHeaderCellDef>Last Run</th>
              <td mat-cell *matCellDef="let t">
                <ng-container *ngIf="t.lastRun; else never">
                  <span class="status" [ngClass]="'status-' + t.lastRun.status">
                    {{ statusLabel(t.lastRun.status) }}
                  </span>
                  <div class="task-sub">{{ asDate(t.lastRun.startedAt) | date:'medium' }}</div>
                </ng-container>
                <ng-template #never>—</ng-template>
              </td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>Actions</th>
              <td mat-cell *matCellDef="let t">
                <button mat-icon-button matTooltip="Run now" (click)="runNow(t)"
                        [disabled]="runningIds.has(t.id)">
                  <mat-icon>play_arrow</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Run history"
                        [routerLink]="['/admin/scheduled-tasks', t.id, 'runs']">
                  <mat-icon>history</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Edit"
                        [routerLink]="['/admin/scheduled-tasks/edit', t.id]">
                  <mat-icon>edit</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Delete" color="warn" (click)="deleteTask(t)">
                  <mat-icon>delete</mat-icon>
                </button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>

            <tr class="mat-row no-data-row" *matNoDataRow>
              <td class="mat-cell no-data-cell" [attr.colspan]="displayedColumns.length">
                No scheduled tasks yet. Create one to export query results on a schedule.
              </td>
            </tr>
          </table>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }
    .loading { display: flex; justify-content: center; padding: 40px; }
    table { width: 100%; }
    .task-name { font-weight: 500; }
    .task-sub { font-size: 12px; color: var(--text-secondary); }
    .enabled { color: #4caf50; }
    .disabled { color: var(--text-secondary); }
    .status { font-weight: 500; }
    .status-Succeeded { color: #4caf50; }
    .status-PartiallySucceeded { color: #ff9800; }
    .status-Failed { color: #f44336; }
    .status-Running { color: #2196f3; }
    .no-data-row { height: 56px; }
    .no-data-cell { text-align: center; color: var(--text-secondary); padding: 16px; }
  `]
})
export class ScheduledTasksListComponent implements OnInit {
  displayedColumns = ['name', 'schedule', 'enabled', 'nextRun', 'lastRun', 'actions'];
  tasks: ScheduledTask[] = [];
  loading = true;
  runningIds = new Set<string>();

  constructor(
    private scheduledTaskService: ScheduledTaskService,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.scheduledTaskService.getAll().subscribe({
      next: (tasks) => {
        this.tasks = tasks;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.snackBar.open('Failed to load scheduled tasks', 'Close', { duration: 5000 });
        this.cdr.detectChanges();
      }
    });
  }

  describe(task: ScheduledTask): string {
    return describeSchedule(task);
  }

  asDate(value: string): Date {
    return utcDate(value);
  }

  statusLabel(status: string): string {
    return status === 'PartiallySucceeded' ? 'Partially succeeded' : status;
  }

  runNow(task: ScheduledTask): void {
    this.runningIds.add(task.id);
    this.scheduledTaskService.runNow(task.id).subscribe({
      next: () => {
        this.runningIds.delete(task.id);
        this.snackBar.open(`"${task.name}" queued — check its run history for the result`, 'Close', { duration: 4000 });
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.runningIds.delete(task.id);
        this.snackBar.open(err?.error?.message || 'Failed to queue the run', 'Close', { duration: 5000 });
        this.cdr.detectChanges();
      }
    });
  }

  deleteTask(task: ScheduledTask): void {
    if (!confirm(`Delete scheduled task "${task.name}" and its run history?`)) return;

    this.scheduledTaskService.delete(task.id).subscribe({
      next: () => {
        this.snackBar.open('Scheduled task deleted', 'Close', { duration: 3000 });
        this.load();
      },
      error: () => {
        this.snackBar.open('Failed to delete scheduled task', 'Close', { duration: 5000 });
      }
    });
  }
}
