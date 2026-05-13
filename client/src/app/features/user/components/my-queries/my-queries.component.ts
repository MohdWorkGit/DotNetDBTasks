import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { MyQueryGroup } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-my-queries',
  template: `
    <div class="container">
      <h2>My Queries</h2>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <mat-card *ngIf="!loading && errorMessage" class="error-card">
        <mat-card-content>
          <p class="error-text">{{ errorMessage }}</p>
          <button mat-raised-button color="primary" (click)="loadGroups()">
            <mat-icon>refresh</mat-icon> Retry
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
                {{ group.name }}
              </mat-panel-title>
              <mat-panel-description>
                <span class="query-count">{{ group.queries.length }} {{ group.queries.length === 1 ? 'query' : 'queries' }}</span>
                <span *ngIf="group.description" class="group-desc">{{ group.description }}</span>
              </mat-panel-description>
            </mat-expansion-panel-header>

            <div class="query-grid">
              <mat-card *ngFor="let query of group.queries" class="query-card">
                <mat-card-header>
                  <mat-card-title>{{ query.name }}</mat-card-title>
                </mat-card-header>
                <mat-card-content>
                  <p>{{ query.description }}</p>
                  <mat-chip-set>
                    <mat-chip *ngFor="let p of query.parameters">
                      {{ p.displayName }}
                    </mat-chip>
                  </mat-chip-set>
                </mat-card-content>
                <mat-card-actions align="end">
                  <button mat-raised-button color="primary"
                          [routerLink]="['/user/queries', query.id, 'execute']">
                    <mat-icon>play_arrow</mat-icon> Execute
                  </button>
                </mat-card-actions>
              </mat-card>
            </div>
          </mat-expansion-panel>
        </mat-accordion>

        <mat-card *ngIf="groups.length === 0">
          <mat-card-content>
            <p>No queries assigned to you yet.</p>
          </mat-card-content>
        </mat-card>
      </ng-container>
    </div>
  `,
  styles: [`
    .loading { display: flex; justify-content: center; padding: 40px; }
    .error-card { margin-bottom: 16px; }
    .error-text { color: var(--status-error); margin-bottom: 16px; }
    .groups { display: block; }
    .folder-icon { margin-right: 8px; vertical-align: middle; color: var(--text-secondary); }
    .query-count { font-size: 13px; color: var(--text-secondary); margin-right: 12px; }
    .group-desc { color: var(--text-secondary); }
    .query-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
      gap: 16px;
      padding-top: 12px;
    }
    .query-card { height: 100%; }
  `]
})
export class MyQueriesComponent implements OnInit {
  groups: MyQueryGroup[] = [];
  loading = true;
  errorMessage = '';

  constructor(
    private queryService: QueryService,
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
          return throwError(() => ({ error: { message: 'Request timed out. Please try again.' } }));
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
        this.errorMessage = err.error?.message || 'Failed to load queries. Please try again.';
        this.cdr.detectChanges();
      }
    });
  }
}
