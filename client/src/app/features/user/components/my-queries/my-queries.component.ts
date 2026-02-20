import { Component, OnInit } from '@angular/core';
import { QueryService } from '@core/services/query.service';
import { DynamicQuery } from '@core/models/dynamic-query.model';

@Component({
  selector: 'app-my-queries',
  template: `
    <div class="container">
      <h2>My Queries</h2>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <div class="query-grid" *ngIf="!loading">
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

      <mat-card *ngIf="!loading && queries.length === 0">
        <mat-card-content>
          <p>No queries assigned to you yet.</p>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .loading { display: flex; justify-content: center; padding: 40px; }
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

  constructor(private queryService: QueryService) {}

  ngOnInit(): void {
    this.queryService.getMyQueries().subscribe({
      next: (q) => { this.queries = q; this.loading = false; },
      error: () => this.loading = false
    });
  }
}
