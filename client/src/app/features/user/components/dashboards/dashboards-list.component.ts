import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { DashboardService } from '@core/services/dashboard.service';
import { extractApiError } from '@core/services/toast.service';
import { DashboardSummary } from '@core/models/dashboard.model';

/** The dashboards shared with this account. */
@Component({
  selector: 'app-dashboards-list',
  standalone: false,
  template: `
    <div class="container">
      <div class="page-header">
        <h1>{{ 'user.dashboards.title' | transloco }}</h1>
      </div>

      <div class="loading" *ngIf="loading">
        <mat-progress-spinner mode="indeterminate" diameter="40"></mat-progress-spinner>
      </div>

      <div class="error-block" *ngIf="error">
        <span class="error-text">{{ error }}</span>
      </div>

      <p class="muted" *ngIf="!loading && !error && dashboards.length === 0">
        {{ 'user.dashboards.none' | transloco }}
      </p>

      <div class="cards">
        <a class="card" *ngFor="let d of dashboards" [routerLink]="['/user/dashboards', d.id]">
          <mat-icon class="card-icon" aria-hidden="true">space_dashboard</mat-icon>
          <div class="card-text">
            <strong dir="auto">{{ d.name }}</strong>
            <small dir="auto" *ngIf="d.description">{{ d.description }}</small>
            <small class="meta">
              {{ 'user.dashboards.tileCount' | transloco: { count: d.tileCount } }}
              · {{ 'user.dashboards.refreshEvery' | transloco: { seconds: d.defaultRefreshSeconds } }}
            </small>
          </div>
        </a>
      </div>
    </div>
  `,
  styles: [`
    .muted { color: var(--text-secondary); }
    .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 14px; }
    .card {
      display: flex; gap: 12px; align-items: flex-start; padding: 16px;
      border-radius: 8px; border: 1px solid var(--border-color); background: var(--bg-card);
      color: var(--text-primary); text-decoration: none;
    }
    .card:hover, .card:focus-visible { border-color: var(--accent-primary); }
    .card:focus-visible { outline: 2px solid var(--accent-primary); outline-offset: 2px; }
    .card-icon { color: var(--accent-primary); flex: none; }
    .card-text { display: flex; flex-direction: column; gap: 4px; min-inline-size: 0; }
    .card-text small { color: var(--text-secondary); }
    .meta { font-size: 12px; }
  `]
})
export class DashboardsListComponent implements OnInit {
  private service = inject(DashboardService);
  private transloco = inject(TranslocoService);
  private cdr = inject(ChangeDetectorRef);

  dashboards: DashboardSummary[] = [];
  loading = true;
  error = '';

  ngOnInit(): void {
    this.service.getMine().subscribe({
      next: dashboards => {
        this.dashboards = dashboards;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: err => {
        this.error = extractApiError(err, this.transloco.translate('user.dashboards.loadFailed'));
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }
}
