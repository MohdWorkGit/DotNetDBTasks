import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { DynamicQuery } from '@core/models/dynamic-query.model';

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
          <button mat-raised-button color="primary" (click)="loadQueries()">
            <mat-icon>refresh</mat-icon> Retry
          </button>
        </mat-card-content>
      </mat-card>

      <div class="query-grid" *ngIf="!loading && !errorMessage">
        <mat-card *ngFor="let query of queries" class="query-card">
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

      <mat-card *ngIf="!loading && !errorMessage && queries.length === 0">
        <mat-card-content>
          <p>No queries assigned to you yet.</p>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .loading { display: flex; justify-content: center; padding: 40px; }
    .error-card { margin-bottom: 16px; }
    .error-text { color: #f44336; margin-bottom: 16px; }
    .query-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
      gap: 16px;
    }
    .query-card { height: 100%; }
  `]
})
export class MyQueriesComponent implements OnInit {
  queries: DynamicQuery[] = [];
  loading = true;
  errorMessage = '';

  constructor(
    private queryService: QueryService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.loadQueries();
  }

  loadQueries(): void {
    this.loading = true;
    this.errorMessage = '';
    this.queryService.getMyQueries().pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out. Please try again.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (q) => {
        this.queries = q;
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
