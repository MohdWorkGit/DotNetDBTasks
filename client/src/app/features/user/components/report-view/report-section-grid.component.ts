import {
  Component, Input, OnInit, OnDestroy, ViewChild, ChangeDetectorRef, inject
} from '@angular/core';
import { MatPaginator, PageEvent } from '@angular/material/paginator';
import { Sort } from '@angular/material/sort';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { QueryService } from '@core/services/query.service';
import { extractApiError } from '@core/services/toast.service';
import { TranslocoService } from '@jsverse/transloco';

/**
 * One report section's rows.
 *
 * <p>Pages against the report run's cached result through the <i>query</i> job endpoints, since
 * that is where a report's sections are filed. Sorting and column filtering therefore behave
 * identically to the query grid, and only one page of rows ever crosses the wire even though
 * the section may hold the complete result set.</p>
 */
@Component({
  selector: 'app-report-section-grid',
  standalone: false,
  template: `
    <div class="error-block" *ngIf="error">
      <span class="error-text">{{ error }}</span>
    </div>

    <div class="table-wrapper" *ngIf="columns.length > 0">
      <table mat-table [dataSource]="rows" matSort (matSortChange)="onSortChange($event)">
        <ng-container *ngFor="let col of columns" [matColumnDef]="col">
          <th mat-header-cell *matHeaderCellDef mat-sort-header>{{ col }}</th>
          <td mat-cell *matCellDef="let row" dir="auto">{{ row[col] }}</td>
        </ng-container>

        <ng-container *ngFor="let col of columns" [matColumnDef]="'filter_' + col">
          <th mat-header-cell *matHeaderCellDef>
            <input class="col-filter-input"
                   [attr.aria-label]="('user.reports.filterColumn' | transloco) + ' ' + col"
                   (input)="applyColumnFilter($event, col)" />
          </th>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-header-row *matHeaderRowDef="filterColumns" class="filter-row"></tr>
        <tr mat-row *matRowDef="let row; columns: columns;"></tr>
        <tr class="mat-row no-data-row" *matNoDataRow>
          <td class="no-data-cell" [attr.colspan]="columns.length">
            {{ 'user.reports.sectionEmpty' | transloco }}
          </td>
        </tr>
      </table>
    </div>

    <mat-paginator *ngIf="columns.length > 0"
                   [length]="filteredTotal"
                   [pageSize]="pageSize"
                   [pageSizeOptions]="[10, 25, 50, 100]"
                   (page)="onPage($event)"
                   showFirstLastButtons></mat-paginator>
  `,
  styles: [`
    .col-filter-input {
      inline-size: 100%;
      box-sizing: border-box;
      padding: 4px 6px;
      border: 1px solid var(--border-color);
      border-radius: 4px;
      background: var(--bg-surface);
      color: var(--text-primary);
    }
  `]
})
export class ReportSectionGridComponent implements OnInit, OnDestroy {
  /** The cached-result job holding this section's rows. */
  @Input({ required: true }) jobId!: string;
  @Input() columns: string[] = [];

  private queryService = inject(QueryService);
  private transloco = inject(TranslocoService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild(MatPaginator) paginator?: MatPaginator;

  rows: Record<string, any>[] = [];
  filteredTotal = 0;
  pageSize = 25;
  error = '';

  private pageIndex = 0;
  private sortColumn: string | null = null;
  private sortDir: 'asc' | 'desc' | null = null;
  private filters: Record<string, string> = {};
  private filterChange$ = new Subject<void>();
  private subscription = new Subscription();

  get filterColumns(): string[] {
    return this.columns.map(c => 'filter_' + c);
  }

  ngOnInit(): void {
    // Debounced so typing in a column filter does not fire a request per keystroke.
    this.subscription.add(
      this.filterChange$.pipe(debounceTime(300)).subscribe(() => {
        this.pageIndex = 0;
        this.paginator?.firstPage();
        this.load();
      })
    );
    this.load();
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.load();
  }

  onSortChange(sort: Sort): void {
    this.sortColumn = sort.direction ? sort.active : null;
    this.sortDir = (sort.direction || null) as 'asc' | 'desc' | null;
    this.pageIndex = 0;
    this.load();
  }

  applyColumnFilter(event: Event, column: string): void {
    this.filters[column] = (event.target as HTMLInputElement).value;
    this.filterChange$.next();
  }

  private load(): void {
    this.queryService.getJobRows(this.jobId, {
      pageIndex: this.pageIndex,
      pageSize: this.pageSize,
      // JobRowsQuery treats these as optional-undefined, not nullable.
      sortColumn: this.sortColumn ?? undefined,
      sortDir: this.sortDir ?? undefined,
      filters: this.filters
    }).subscribe({
      next: page => {
        this.rows = page.rows;
        this.filteredTotal = page.filteredTotal;
        this.error = '';
        this.cdr.detectChanges();
      },
      error: err => {
        this.error = extractApiError(err, this.transloco.translate('user.reports.sectionFailed'));
        this.cdr.detectChanges();
      }
    });
  }
}
