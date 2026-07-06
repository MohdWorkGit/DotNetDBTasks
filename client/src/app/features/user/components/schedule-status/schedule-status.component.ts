import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ScheduledTaskService } from '@core/services/scheduled-task.service';
import {
  ScheduledTask,
  ScheduledTaskRun,
  describeSchedule,
  utcDate
} from '@core/models/scheduled-task.model';

/**
 * Read-only scheduled task status for regular users. Only tasks the admin granted
 * this user viewer permission on are returned by the API.
 */
@Component({
  standalone: false,
  selector: 'app-schedule-status',
  template: `
    <div class="container">
      <div class="header">
        <h2>Scheduled Tasks</h2>
        <button mat-icon-button matTooltip="Refresh" (click)="load()">
          <mat-icon>refresh</mat-icon>
        </button>
      </div>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <p class="hint" *ngIf="!loading && tasks.length === 0">
        No scheduled tasks have been shared with you.
      </p>

      <mat-accordion *ngIf="!loading">
        <mat-expansion-panel *ngFor="let task of tasks" (opened)="loadRuns(task)">
          <mat-expansion-panel-header>
            <mat-panel-title>{{ task.name }}</mat-panel-title>
            <mat-panel-description>
              {{ describe(task) }}
              <span *ngIf="task.lastRun" class="status" [ngClass]="'status-' + task.lastRun.status">
                &nbsp;· {{ statusLabel(task.lastRun.status) }}
                {{ asDate(task.lastRun.startedAt) | date:'short' }}
              </span>
              <span *ngIf="!task.isEnabled">&nbsp;· disabled</span>
            </mat-panel-description>
          </mat-expansion-panel-header>

          <p class="hint" *ngIf="task.description">{{ task.description }}</p>
          <p class="hint" *ngIf="task.isEnabled && task.nextRunAt">
            Next run: {{ asDate(task.nextRunAt) | date:'medium' }}
          </p>

          <div *ngIf="runsLoading[task.id]" class="loading">
            <mat-spinner diameter="24"></mat-spinner>
          </div>

          <table class="runs" *ngIf="runs[task.id]?.length">
            <tr>
              <th>Started</th>
              <th>Status</th>
              <th>Trigger</th>
              <th>Files</th>
            </tr>
            <tr *ngFor="let run of runs[task.id]">
              <td>{{ asDate(run.startedAt) | date:'medium' }}</td>
              <td>
                <span class="status" [ngClass]="'status-' + run.status">{{ statusLabel(run.status) }}</span>
              </td>
              <td>{{ run.triggeredByUsername ? 'manual' : 'scheduled' }}</td>
              <td>{{ fileSummary(run) }}</td>
            </tr>
          </table>
          <p class="hint" *ngIf="runs[task.id] && runs[task.id].length === 0">
            This task has not run yet.
          </p>
        </mat-expansion-panel>
      </mat-accordion>
    </div>
  `,
  styles: [`
    .header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }
    .loading { display: flex; justify-content: center; padding: 24px; }
    .hint { color: var(--text-secondary); }
    .status { font-weight: 500; }
    .status-Succeeded { color: #4caf50; }
    .status-PartiallySucceeded { color: #ff9800; }
    .status-Failed { color: #f44336; }
    .status-Running { color: #2196f3; }
    table.runs { width: 100%; border-collapse: collapse; }
    table.runs th, table.runs td {
      text-align: left;
      padding: 6px 12px 6px 0;
      border-bottom: 1px solid rgba(128,128,128,.2);
      font-size: 13px;
    }
    table.runs th { color: var(--text-secondary); font-weight: 500; }
  `]
})
export class ScheduleStatusComponent implements OnInit {
  tasks: ScheduledTask[] = [];
  runs: Record<string, ScheduledTaskRun[]> = {};
  runsLoading: Record<string, boolean> = {};
  loading = true;

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
    this.runs = {};
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

  loadRuns(task: ScheduledTask): void {
    if (this.runs[task.id]) return;
    this.runsLoading[task.id] = true;
    this.scheduledTaskService.getRuns(task.id, 20).subscribe({
      next: (runs) => {
        this.runs[task.id] = runs;
        this.runsLoading[task.id] = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.runsLoading[task.id] = false;
        this.snackBar.open('Failed to load run history', 'Close', { duration: 5000 });
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

  fileSummary(run: ScheduledTaskRun): string {
    const ok = run.items.filter(i => i.success).length;
    return `${ok}/${run.items.length} succeeded`;
  }
}
