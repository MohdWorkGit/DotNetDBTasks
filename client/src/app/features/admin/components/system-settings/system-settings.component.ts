import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ToastService } from '@core/services/toast.service';
import { QueryService } from '@core/services/query.service';
import { SystemSettings } from '@core/models/dynamic-query.model';

/**
 * Admin-only runtime toggles.
 *
 * <p>Kept separate from the user and role pages: these change what a whole role is permitted
 * to do, which is a different kind of decision from granting one person one query.</p>
 */
@Component({
  standalone: false,
  selector: 'app-system-settings',
  template: `
    <div class="container">
      <div class="header">
        <h2>{{ 'admin.settings.title' | transloco }}</h2>
      </div>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <mat-card *ngIf="!loading && settings">
        <mat-card-content>
          <div class="setting">
            <mat-slide-toggle
              [checked]="settings.accessManagerCanManageQueryAccess"
              [disabled]="saving"
              (change)="setPerQueryAccess($event.checked)">
              {{ 'admin.settings.perQueryAccess' | transloco }}
            </mat-slide-toggle>
            <p class="hint">{{ 'admin.settings.perQueryAccessHint' | transloco }}</p>
          </div>
        </mat-card-content>
      </mat-card>

      <div *ngIf="!loading && errorMessage" class="error-block">
        <p class="error-text">{{ errorMessage }}</p>
        <button mat-stroked-button (click)="load()">{{ 'common.retry' | transloco }}</button>
      </div>
    </div>
  `,
  styles: [`
    .setting { padding: 8px 0; }
    .hint {
      color: var(--text-secondary);
      font-size: 13px;
      margin: 8px 0 0;
      /* Lines up under the toggle's label rather than its track. */
      padding-inline-start: 52px;
      max-width: 70ch;
    }
  `]
})
export class SystemSettingsComponent implements OnInit {
  settings: SystemSettings | null = null;
  loading = true;
  saving = false;
  errorMessage = '';

  constructor(
    private queryService: QueryService,
    private toast: ToastService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.errorMessage = '';
    this.queryService.getSystemSettings().subscribe({
      next: (settings) => {
        this.settings = settings;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || '';
        this.toast.error(err, 'admin.settings.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  setPerQueryAccess(enabled: boolean): void {
    if (!this.settings) return;

    const previous = this.settings.accessManagerCanManageQueryAccess;
    const next: SystemSettings = { ...this.settings, accessManagerCanManageQueryAccess: enabled };

    this.saving = true;
    this.queryService.updateSystemSettings(next).subscribe({
      next: () => {
        this.settings = next;
        this.saving = false;
        this.toast.success('admin.settings.saved');
        this.cdr.detectChanges();
      },
      error: (err) => {
        // Put the toggle back: leaving it showing the value the server rejected would
        // misreport what a role is actually allowed to do.
        this.settings = { ...next, accessManagerCanManageQueryAccess: previous };
        this.saving = false;
        this.toast.error(err, 'admin.settings.saveFailed');
        this.cdr.detectChanges();
      }
    });
  }
}
