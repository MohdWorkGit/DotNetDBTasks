import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { ToastService } from '@core/services/toast.service';
import { AuthService } from '@core/services/auth.service';
import { ConfirmService } from '@core/services/confirm.service';
import { ScheduledTaskService } from '@core/services/scheduled-task.service';
import { ScheduledTask, describeTriggers, utcDate } from '@core/models/scheduled-task.model';

@Component({
  standalone: false,
  selector: 'app-scheduled-tasks-list',
  template: `
    <div class="container">
      <div class="header">
        <h2>Scheduled Tasks</h2>
        <button mat-raised-button color="primary" routerLink="/admin/scheduled-tasks/create"
                *ngIf="authService.isAdmin()">
          <mat-icon>add_alarm</mat-icon> Create Task
        </button>
      </div>

      <mat-card>
        <mat-card-content>
          <div *ngIf="loading" class="loading">
            <mat-spinner diameter="40"></mat-spinner>
          </div>

          <div class="table-wrapper">
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
                <mat-icon [class.enabled]="t.isEnabled" [class.disabled]="!t.isEnabled"
                          [matTooltip]="t.isEnabled ? 'Enabled' : 'Disabled'"
                          [attr.aria-label]="t.isEnabled ? 'Enabled' : 'Disabled'"
                          role="img">
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
                <button mat-icon-button matTooltip="Run now" aria-label="Run now" (click)="runNow(t)"
                        *ngIf="authService.isAdmin()"
                        [disabled]="runningIds.has(t.id)">
                  <mat-icon>play_arrow</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Run history" aria-label="Run history"
                        [routerLink]="['/admin/scheduled-tasks', t.id, 'runs']">
                  <mat-icon>history</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Edit" aria-label="Edit"
                        *ngIf="authService.isAdmin()"
                        [routerLink]="['/admin/scheduled-tasks/edit', t.id]">
                  <mat-icon>edit</mat-icon>
                </button>
                <button mat-icon-button matTooltip="Delete" aria-label="Delete" color="warn"
                        *ngIf="authService.isAdmin()" (click)="deleteTask(t)">
                  <mat-icon>delete</mat-icon>
                </button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>

            <tr class="mat-row no-data-row" *matNoDataRow>
              <td class="mat-cell no-data-cell" [attr.colspan]="displayedColumns.length">
                No scheduled tasks yet.<span *ngIf="authService.isAdmin()"> Create one to export query results on a schedule.</span>
              </td>
            </tr>
          </table>
          </div>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    table { width: 100%; }
    .task-name { font-weight: 500; }
    .task-sub { font-size: 12px; color: var(--text-secondary); }
    .enabled { color: var(--status-success); }
    .disabled { color: var(--text-secondary); }
    .status { font-weight: 500; }
  `]
})
export class ScheduledTasksListComponent implements OnInit {
  displayedColumns = ['name', 'schedule', 'enabled', 'nextRun', 'lastRun', 'actions'];
  tasks: ScheduledTask[] = [];
  loading = true;
  runningIds = new Set<string>();

  constructor(
    public authService: AuthService,
    private scheduledTaskService: ScheduledTaskService,
    private toast: ToastService,
    private confirmService: ConfirmService,
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
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'Failed to load scheduled tasks');
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

  runNow(task: ScheduledTask): void {
    this.runningIds.add(task.id);
    this.scheduledTaskService.runNow(task.id).subscribe({
      next: () => {
        this.runningIds.delete(task.id);
        this.toast.success(`"${task.name}" queued — check its run history for the result`, 4000);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.runningIds.delete(task.id);
        this.toast.error(err, 'Failed to queue the run');
        this.cdr.detectChanges();
      }
    });
  }

  deleteTask(task: ScheduledTask): void {
    this.confirmService.askThen({
      title: 'Delete scheduled task?',
      message: `"${task.name}" and its entire run history will be permanently deleted. `
        + 'This cannot be undone.',
      confirmText: 'Delete',
      destructive: true
    }, () => {
      this.scheduledTaskService.delete(task.id).subscribe({
        next: () => {
          this.toast.success('Scheduled task deleted');
          this.load();
        },
        error: (err) => {
          this.toast.error(err, 'Failed to delete scheduled task');
        }
      });
    });
  }
}
