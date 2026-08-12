import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ToastService } from '@core/services/toast.service';
import { ScheduledTaskService } from '@core/services/scheduled-task.service';
import {
  ScheduledTask,
  ScheduledTaskRun,
  describeTriggers,
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
        <h2>{{ 'user.schedules.title' | transloco }}</h2>
        <button mat-icon-button [matTooltip]="'common.refresh' | transloco" [attr.aria-label]="'common.refresh' | transloco" (click)="load()">
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

          <p class="hint" *ngIf="task.description" dir="auto">{{ task.description }}</p>
          <p class="hint" *ngIf="task.isEnabled && task.nextRunAt">
            Next run: {{ asDate(task.nextRunAt) | date:'medium' }}
          </p>

          <div *ngIf="runsLoading[task.id]" class="loading">
            <mat-spinner diameter="24"></mat-spinner>
          </div>

          <table class="runs" *ngIf="runs[task.id]?.length">
            <tr>
              <th>{{ 'user.schedules.started' | transloco }}</th>
              <th>{{ 'user.schedules.status' | transloco }}</th>
              <th>{{ 'user.schedules.trigger' | transloco }}</th>
              <th>{{ 'user.schedules.files' | transloco }}</th>
            </tr>
            <tr *ngFor="let run of runs[task.id]">
              <td>{{ asDate(run.startedAt) | date:'medium' }}</td>
              <td>
                <span class="status" [ngClass]="'status-' + run.status">{{ statusLabel(run.status) }}</span>
              </td>
              <td>{{ run.triggeredByUsername ? 'manual' : 'scheduled' }}</td>
              <td>
                <ng-container *ngIf="task.canDownloadFiles && runFiles(run).length; else summary">
                  <button *ngFor="let f of runFiles(run)" type="button" class="file-link"
                          [matTooltip]="'common.download' | transloco"
                          [attr.aria-label]="'Download ' + f"
                          (click)="download(task, run, f)">
                    <mat-icon class="file-icon" inline>download</mat-icon>{{ f }}
                  </button>
                </ng-container>
                <ng-template #summary>{{ fileSummary(run) }}</ng-template>
              </td>
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
    .hint { color: var(--text-secondary); }
    .status { font-weight: 500; }
    table.runs { width: 100%; border-collapse: collapse; }
    table.runs th, table.runs td {
      text-align: start;
      padding: 6px 12px 6px 0;
      border-bottom: 1px solid var(--border-color);
      font-size: 13px;
    }
    table.runs th { color: var(--text-secondary); font-weight: 500; }
    /* A real <button>, not an anchor: these trigger a download, they don't navigate. */
    .file-link {
      color: var(--accent-primary);
      background: none;
      border: none;
      padding: 0;
      font: inherit;
      text-align: start;
      text-decoration: none;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 4px;
      margin-inline-end: 12px;
    }
    .file-link:hover { text-decoration: underline; }
    .file-icon { font-size: 16px; }
  `]
})
export class ScheduleStatusComponent implements OnInit {
  tasks: ScheduledTask[] = [];
  runs: Record<string, ScheduledTaskRun[]> = {};
  runsLoading: Record<string, boolean> = {};
  loading = true;

  constructor(
    private scheduledTaskService: ScheduledTaskService,
    private toast: ToastService,
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
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'user.schedules.loadFailed');
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
      error: (err) => {
        this.runsLoading[task.id] = false;
        this.toast.error(err, 'user.schedules.runsLoadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  describe(task: ScheduledTask): string {
    return describeTriggers(task.triggers);
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

  /** Distinct export files of a run (combined-output runs record the same file on every item). */
  runFiles(run: ScheduledTaskRun): string[] {
    return [...new Set(run.items.filter(i => i.success && i.fileName).map(i => i.fileName!))];
  }

  download(task: ScheduledTask, run: ScheduledTaskRun, fileName: string): void {
    this.scheduledTaskService.downloadRunFile(task.id, run.id, fileName).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => {
        const key = err?.status === 404
          ? 'user.schedules.fileGone'
          : 'user.schedules.downloadFailed';
        this.toast.error(key, key, 6000);
      }
    });
  }
}
