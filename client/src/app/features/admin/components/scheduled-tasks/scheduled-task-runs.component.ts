import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ScheduledTaskService } from '@core/services/scheduled-task.service';
import { ScheduledTask, ScheduledTaskRun, ScheduledTaskRunItem, utcDate } from '@core/models/scheduled-task.model';

@Component({
  standalone: false,
  selector: 'app-scheduled-task-runs',
  template: `
    <div class="container">
      <div class="header">
        <h2>Run History{{ task ? ' — ' + task.name : '' }}</h2>
        <div>
          <button mat-icon-button matTooltip="Refresh" (click)="load()">
            <mat-icon>refresh</mat-icon>
          </button>
          <button mat-button routerLink="/admin/scheduled-tasks">
            <mat-icon>arrow_back</mat-icon> Back
          </button>
        </div>
      </div>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <p class="hint" *ngIf="!loading && runs.length === 0">This task has not run yet.</p>

      <mat-accordion *ngIf="!loading">
        <mat-expansion-panel *ngFor="let run of runs">
          <mat-expansion-panel-header>
            <mat-panel-title>
              <span class="status" [ngClass]="'status-' + run.status">{{ statusLabel(run.status) }}</span>
            </mat-panel-title>
            <mat-panel-description>
              {{ asDate(run.startedAt) | date:'medium' }}
              · {{ run.triggeredByUsername ? 'manual by ' + run.triggeredByUsername : 'scheduled' }}
              <span *ngIf="run.completedAt"> · {{ duration(run) }}</span>
            </mat-panel-description>
          </mat-expansion-panel-header>

          <p class="error" *ngIf="run.error">{{ run.error }}</p>

          <table class="items" *ngIf="run.items.length">
            <tr>
              <th>Query</th>
              <th>File</th>
              <th>Rows</th>
              <th>Checkpoint</th>
              <th>Duration</th>
              <th>Result</th>
            </tr>
            <tr *ngFor="let item of run.items">
              <td>{{ item.queryName }}<span class="muted" *ngIf="item.isWrite"> (data change)</span></td>
              <td>
                <a *ngIf="item.fileName && item.success && task?.canDownloadFiles" class="file-link"
                   href="javascript:void(0)"
                   matTooltip="Download"
                   (click)="download(run, item)">
                  <mat-icon class="file-icon" inline>download</mat-icon>{{ item.fileName }}
                </a>
                <ng-container *ngIf="!(item.fileName && item.success && task?.canDownloadFiles)">
                  {{ item.fileName || (item.isWrite ? 'no file' : '—') }}
                </ng-container>
              </td>
              <td>{{ item.success ? item.rowCount + (item.isWrite ? ' affected' : '') : '—' }}</td>
              <td>{{ item.lastKey || '—' }}</td>
              <td>{{ item.durationMs }} ms</td>
              <td>
                <span class="status" [ngClass]="item.success ? 'status-Succeeded' : 'status-Failed'">
                  {{ item.success ? 'OK' : (item.error || 'Failed') }}
                </span>
              </td>
            </tr>
          </table>
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
    .loading { display: flex; justify-content: center; padding: 40px; }
    .hint { color: var(--text-secondary); }
    .status { font-weight: 500; }
    .status-Succeeded { color: #4caf50; }
    .status-PartiallySucceeded { color: #ff9800; }
    .status-Failed { color: #f44336; }
    .status-Running { color: #2196f3; }
    .error { color: #f44336; }
    table.items { width: 100%; border-collapse: collapse; }
    table.items th, table.items td {
      text-align: left;
      padding: 6px 12px 6px 0;
      border-bottom: 1px solid rgba(128,128,128,.2);
      font-size: 13px;
    }
    table.items th { color: var(--text-secondary); font-weight: 500; }
    .muted { color: var(--text-secondary); font-size: 12px; }
    .file-link {
      color: #2196f3;
      text-decoration: none;
      cursor: pointer;
      display: inline-flex;
      align-items: center;
      gap: 4px;
    }
    .file-link:hover { text-decoration: underline; }
    .file-icon { font-size: 16px; }
  `]
})
export class ScheduledTaskRunsComponent implements OnInit {
  task: ScheduledTask | null = null;
  runs: ScheduledTaskRun[] = [];
  loading = true;
  private taskId = '';

  constructor(
    private route: ActivatedRoute,
    private scheduledTaskService: ScheduledTaskService,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.taskId = this.route.snapshot.paramMap.get('id')!;
    this.load();
  }

  load(): void {
    this.loading = true;
    this.scheduledTaskService.getById(this.taskId).subscribe({
      next: (task) => {
        this.task = task;
        this.cdr.detectChanges();
      }
    });
    this.scheduledTaskService.getRuns(this.taskId).subscribe({
      next: (runs) => {
        this.runs = runs;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.snackBar.open('Failed to load run history', 'Close', { duration: 5000 });
        this.cdr.detectChanges();
      }
    });
  }

  download(run: ScheduledTaskRun, item: ScheduledTaskRunItem): void {
    const fileName = item.fileName!;
    this.scheduledTaskService.downloadRunFile(this.taskId, run.id, fileName).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => {
        const message = err?.status === 404
          ? 'The file is no longer available on the server (it may have been moved, deleted or overwritten by a newer run).'
          : 'Failed to download the file';
        this.snackBar.open(message, 'Close', { duration: 6000 });
      }
    });
  }

  asDate(value: string): Date {
    return utcDate(value);
  }

  statusLabel(status: string): string {
    return status === 'PartiallySucceeded' ? 'Partially succeeded' : status;
  }

  duration(run: ScheduledTaskRun): string {
    if (!run.completedAt) return '';
    const ms = utcDate(run.completedAt).getTime() - utcDate(run.startedAt).getTime();
    return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
  }
}
