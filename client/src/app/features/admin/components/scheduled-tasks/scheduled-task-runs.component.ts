import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ToastService } from '@core/services/toast.service';
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
          <button mat-icon-button matTooltip="Refresh" aria-label="Refresh" (click)="load()">
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
            <button *ngIf="run.status === 'Running'" mat-stroked-button color="warn" class="cancel-btn"
                    (click)="$event.stopPropagation(); cancel(run)" [disabled]="cancelingId === run.id">
              <mat-icon>stop</mat-icon> {{ cancelingId === run.id ? 'Canceling…' : 'Cancel' }}
            </button>
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
                <button *ngIf="item.fileName && item.success && task?.canDownloadFiles"
                        type="button" class="file-link"
                        matTooltip="Download"
                        [attr.aria-label]="'Download ' + item.fileName"
                        (click)="download(run, item)">
                  <mat-icon class="file-icon" inline>download</mat-icon>{{ item.fileName }}
                </button>
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
    .hint { color: var(--text-secondary); }
    .status { font-weight: 500; }
    .error { color: var(--status-error); }
    .cancel-btn { margin-left: 12px; line-height: 30px; }
    table.items { width: 100%; border-collapse: collapse; }
    table.items th, table.items td {
      text-align: left;
      padding: 6px 12px 6px 0;
      border-bottom: 1px solid var(--border-color);
      font-size: 13px;
    }
    table.items th { color: var(--text-secondary); font-weight: 500; }
    .muted { color: var(--text-secondary); font-size: 12px; }
    /* A real <button>, not an anchor: these trigger a download, they don't navigate. */
    .file-link {
      color: var(--accent-primary);
      background: none;
      border: none;
      padding: 0;
      font: inherit;
      text-align: left;
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
  cancelingId: string | null = null;
  private taskId = '';

  constructor(
    private route: ActivatedRoute,
    private scheduledTaskService: ScheduledTaskService,
    private toast: ToastService,
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
      },
      // Without this the header stayed blank and canDownloadFiles was undefined,
      // so every download link silently vanished with no explanation.
      error: (err) => {
        this.toast.error(err, 'Failed to load the scheduled task');
        this.cdr.detectChanges();
      }
    });
    this.scheduledTaskService.getRuns(this.taskId).subscribe({
      next: (runs) => {
        this.runs = runs;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'Failed to load run history');
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
        this.toast.error(message, message, 6000);
      }
    });
  }

  cancel(run: ScheduledTaskRun): void {
    this.cancelingId = run.id;
    this.scheduledTaskService.cancelRun(this.taskId, run.id).subscribe({
      next: () => {
        this.cancelingId = null;
        this.toast.success('Cancellation requested — the running query is being stopped.', 4000);
        this.load();
      },
      error: (err) => {
        this.cancelingId = null;
        const message = err?.status === 409
          ? 'That run is no longer in progress.'
          : 'Failed to cancel the run';
        this.toast.error(message, message);
        this.load();
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
