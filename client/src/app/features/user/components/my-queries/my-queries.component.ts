import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { MyQueryGroup } from '@core/models/dynamic-query.model';
import { ReportSummary } from '@core/models/report.model';
import { TranslocoService } from '@jsverse/transloco';

@Component({
  standalone: false,
  selector: 'app-my-queries',
  template: `
    <div class="container">
      <h2>{{ 'user.queries.title' | transloco }}</h2>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <mat-card *ngIf="!loading && errorMessage" class="error-card">
        <mat-card-content>
          <p class="error-text">{{ errorMessage }}</p>
          <button mat-raised-button color="primary" (click)="loadGroups()">
            <mat-icon>refresh</mat-icon> {{ 'common.retry' | transloco }}
          </button>
        </mat-card-content>
      </mat-card>

      <ng-container *ngIf="!loading && !errorMessage">
        <mat-accordion multi class="groups">
          <mat-expansion-panel *ngFor="let group of groups; let first = first"
                               [expanded]="first">
            <mat-expansion-panel-header>
              <mat-panel-title>
                <mat-icon class="folder-icon">{{ group.id ? 'folder' : 'folder_open' }}</mat-icon>
                <span dir="auto">{{ group.name }}</span>
              </mat-panel-title>
              <mat-panel-description>
                <span class="query-count">{{ (itemCount(group) === 1 ? 'user.queries.countOne' : 'user.queries.countMany')
              | transloco: { count: itemCount(group) } }}</span>
                <span *ngIf="group.description" class="group-desc" dir="auto">{{ group.description }}</span>
              </mat-panel-description>
            </mat-expansion-panel-header>

            <div class="query-grid">
              <mat-card *ngFor="let query of group.queries" class="query-card">
                <mat-card-header>
                  <mat-card-title dir="auto">{{ query.name }}</mat-card-title>
                </mat-card-header>
                <mat-card-content>
                  <p dir="auto">{{ query.description }}</p>
                  <mat-chip-set>
                    <mat-chip *ngFor="let p of query.parameters">
                      {{ p.displayName }}
                    </mat-chip>
                  </mat-chip-set>
                </mat-card-content>
                <mat-card-actions align="end">
                  <a mat-raised-button color="primary"
                     [routerLink]="['/user/queries', query.id, 'execute']">
                    <mat-icon>play_arrow</mat-icon> {{ 'user.queries.execute' | transloco }}
                  </a>
                </mat-card-actions>
              </mat-card>

              <!-- Reports sit in the same grid as the queries. The only thing that marks one
                   out is the chip saying how many datasets it pulls together. -->
              <mat-card *ngFor="let report of group.reports" class="query-card">
                <mat-card-header>
                  <mat-card-title dir="auto">{{ report.name }}</mat-card-title>
                </mat-card-header>
                <mat-card-content>
                  <p dir="auto">{{ report.description }}</p>
                  <mat-chip-set>
                    <mat-chip class="report-chip">
                      <mat-icon class="chip-icon">summarize</mat-icon>
                      {{ 'user.queries.reportBadge' | transloco: { count: report.datasetCount } }}
                    </mat-chip>
                  </mat-chip-set>
                </mat-card-content>
                <mat-card-actions align="end">
                  <a mat-raised-button color="primary"
                     [routerLink]="['/user/reports', report.id, 'view']">
                    <mat-icon>play_arrow</mat-icon> {{ 'user.queries.execute' | transloco }}
                  </a>
                </mat-card-actions>
              </mat-card>
            </div>
          </mat-expansion-panel>
        </mat-accordion>

        <mat-card *ngIf="groups.length === 0">
          <mat-card-content>
            <p>{{ 'user.queries.none' | transloco }}</p>
          </mat-card-content>
        </mat-card>
      </ng-container>
    </div>
  `,
  styles: [`
    .error-card { margin-bottom: 16px; }
    .groups { display: block; }
    .folder-icon { margin-inline-end: 8px; vertical-align: middle; color: var(--text-secondary); }
    .query-count { font-size: 13px; color: var(--text-secondary); margin-inline-end: 12px; }
    .group-desc { color: var(--text-secondary); }
    .query-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
      gap: 16px;
      padding-top: 12px;
    }
    .query-card { height: 100%; }
    .report-chip { background: var(--chip-accent); }
    .chip-icon { font-size: 16px; inline-size: 16px; block-size: 16px; margin-inline-end: 4px; }
  `]
})
export class MyQueriesComponent implements OnInit {
  groups: MyQueryGroup[] = [];

  /** Queries and reports together — the panel header counts what is inside, not what kind. */
  itemCount(group: MyQueryGroup): number {
    return group.queries.length + (group.reports?.length ?? 0);
  }

  loading = true;
  errorMessage = '';

  constructor(
    private queryService: QueryService,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.loadGroups();
  }

  loadGroups(): void {
    this.loading = true;
    this.errorMessage = '';
    this.queryService.getMyQueryGroups().pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: this.transloco.translate('common.requestTimedOut') } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (groups) => {
        this.groups = groups;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.errorMessage = err.error?.message || this.transloco.translate('user.queries.loadFailed');
        this.cdr.detectChanges();
      }
    });
  }
}
