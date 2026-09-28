import {
  Component, Input, Output, EventEmitter, OnChanges, OnDestroy, OnInit, SimpleChanges,
  ChangeDetectorRef, inject
} from '@angular/core';
import { Subject, Subscription, of } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { TranslocoService } from '@jsverse/transloco';
import { DashboardService } from '@core/services/dashboard.service';
import { ToastService, extractApiError } from '@core/services/toast.service';
import { AuthService } from '@core/services/auth.service';
import { QueryService } from '@core/services/query.service';
import { EXPORT_FORMATS, ExportFormatOption } from '@core/models/export-formats';
import { PERM } from '@core/models/permissions';
import { toIsoDate } from '@core/models/local-date';
import {
  DashboardDrillAction, DashboardTile, DashboardTileData, matchRule
} from '@core/models/dashboard.model';

/**
 * One tile: fetches its own data and keeps it current.
 *
 * <p>The next fetch is timed from the server's <c>nextRefreshAt</c> rather than a fixed client
 * interval. The server shares each result between viewers, so asking before it expires would only
 * return the same answer; asking right after it does is what makes the tile "live". A little
 * jitter keeps every open browser from arriving in the same instant.</p>
 *
 * <p>Nothing is fetched while the tab is hidden. The due fetch is held and made the moment the
 * tab is shown again, so a dashboard left open in a background tab costs nothing, and one brought
 * back to the front is current straight away.</p>
 */
@Component({
  selector: 'app-dashboard-tile',
  standalone: false,
  template: `
    <section class="tile" [attr.aria-labelledby]="'tile-title-' + tile.id" [attr.aria-busy]="loading">
      <header class="tile-header">
        <h2 class="tile-title" [id]="'tile-title-' + tile.id" dir="auto">{{ tile.title }}</h2>
        <mat-icon *ngIf="stale" class="stale-icon"
                  [matTooltip]="'user.dashboards.staleHint' | transloco: { error: staleError }">
          sync_problem
        </mat-icon>
        <span class="updated" *ngIf="data && !data.error">
          {{ 'user.dashboards.updated' | transloco: { time: (data.generatedAt | date: 'mediumTime') } }}
        </span>
        <button mat-icon-button type="button" class="tile-menu" [matMenuTriggerFor]="tileMenu"
                [disabled]="downloading"
                [attr.aria-label]="'user.dashboards.tileMenu' | transloco: { title: tile.title }">
          <mat-icon>{{ downloading ? 'hourglass_top' : 'more_vert' }}</mat-icon>
        </button>
        <mat-menu #tileMenu="matMenu">
          <button mat-menu-item (click)="drill.emit(null)">
            <mat-icon>table_rows</mat-icon> {{ 'user.dashboards.showRows' | transloco }}
          </button>
          <button mat-menu-item *ngFor="let f of downloadFormats" (click)="download(f)">
            <mat-icon>{{ f.icon }}</mat-icon>
            {{ 'user.dashboards.downloadAs' | transloco: { format: (f.labelKey | transloco) } }}
          </button>
        </mat-menu>
      </header>

      <div class="tile-body">
        <div class="tile-state" *ngIf="!data && loading">
          <mat-progress-spinner mode="indeterminate" diameter="28"></mat-progress-spinner>
        </div>

        <div class="tile-state error-text" *ngIf="data?.error" role="alert">
          <mat-icon aria-hidden="true">error_outline</mat-icon>
          <span dir="auto">{{ data?.error }}</span>
        </div>

        <div class="tile-state muted" *ngIf="data && !data.error && data.isEmpty">
          {{ 'user.dashboards.noData' | transloco }}
        </div>

        <ng-container *ngIf="data && !data.error && !data.isEmpty">
          <app-kpi-tile *ngIf="data.kpi" [kpi]="data.kpi" [rules]="tile.conditionalRules"
                        [format]="tile.valueFormat" [higherIsBetter]="tile.higherIsBetter"></app-kpi-tile>

          <app-report-chart *ngIf="data.chart" [chart]="data.chart" [compact]="true"
                            [interactive]="drillable" (categoryClick)="drill.emit($event)"></app-report-chart>

          <div class="tile-table" *ngIf="data.table">
            <table>
              <thead>
                <tr><th *ngFor="let c of data.table.columns" dir="auto">{{ c }}</th></tr>
              </thead>
              <tbody>
                <tr *ngFor="let row of data.table.rows">
                  <td *ngFor="let c of data.table.columns" dir="auto" [ngClass]="toneClass(c, row[c])">{{ row[c] }}</td>
                </tr>
              </tbody>
            </table>
            <p class="muted more" *ngIf="data.table.totalRows > data.table.rows.length">
              {{ 'user.dashboards.tableMore' | transloco: { shown: data.table.rows.length, total: data.table.totalRows } }}
            </p>
          </div>
        </ng-container>
      </div>
    </section>
  `,
  styles: [`
    :host { display: block; min-inline-size: 0; }
    .tile {
      display: flex; flex-direction: column; block-size: 100%; box-sizing: border-box;
      padding: 12px 14px; border-radius: 8px;
      background: var(--bg-card); border: 1px solid var(--border-color);
    }
    .tile-header { display: flex; align-items: center; gap: 8px; min-block-size: 24px; }
    .tile-title {
      flex: 1 1 auto; margin: 0; font-size: 15px; font-weight: 600; color: var(--text-primary);
      overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
    }
    .updated { font-size: 11px; color: var(--text-secondary); white-space: nowrap; }
    .stale-icon { color: var(--status-warning); font-size: 18px; inline-size: 18px; block-size: 18px; }
    .tile-body { flex: 1 1 auto; min-block-size: 0; margin-block-start: 8px; position: relative; }
    .tile-state {
      display: flex; align-items: center; justify-content: center; gap: 8px;
      block-size: 100%; text-align: center; padding: 8px;
    }
    .muted { color: var(--text-secondary); }
    .tile-table { block-size: 100%; overflow: auto; }
    .tile-table table { inline-size: 100%; border-collapse: collapse; font-size: 13px; }
    .tile-table th, .tile-table td {
      padding: 4px 8px; text-align: start; border-block-end: 1px solid var(--divider-color);
      white-space: nowrap;
    }
    .tile-table th { position: sticky; inset-block-start: 0; background: var(--bg-card); color: var(--text-secondary); font-weight: 600; }
    .more { font-size: 12px; margin: 6px 0 0; }
    .tile-menu { margin-inline-end: -8px; inline-size: 32px; block-size: 32px; padding: 4px; }
    .tile-menu mat-icon { font-size: 20px; inline-size: 20px; block-size: 20px; }
    /* Colour rules: foreground tokens, with weight so the tone is not carried by hue alone. */
    .tone-good { color: var(--status-success); font-weight: 600; }
    .tone-warn { color: var(--status-warning); font-weight: 600; }
    .tone-bad { color: var(--status-error); font-weight: 600; }
  `]
})
export class DashboardTileComponent implements OnInit, OnChanges, OnDestroy {
  @Input({ required: true }) dashboardId!: string;
  @Input({ required: true }) tile!: DashboardTile;
  /** The applied filter values, in the wire format. A new object re-fetches immediately. */
  @Input() filters: Record<string, string> = {};
  /** Bumped by the page's "Refresh all" button. */
  @Input() refreshNonce = 0;

  /** A category was activated (chart drill), or null for the tile's "show rows" button. */
  @Output() drill = new EventEmitter<string | null>();

  private dashboards = inject(DashboardService);
  private queries = inject(QueryService);
  private auth = inject(AuthService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private cdr = inject(ChangeDetectorRef);

  /**
   * Both export gates, as the query page applies them: the query must permit the format and the
   * viewer's roles must hold its capability. Downloading goes through the query export endpoint,
   * which also needs queries.run, so without it nothing is offered.
   */
  downloadFormats: ExportFormatOption[] = [];
  downloading = false;

  data: DashboardTileData | null = null;
  loading = false;
  /** The last fetch failed at the HTTP level; the previous data is still on screen. */
  stale = false;
  staleError = '';

  private load$ = new Subject<void>();
  private subscription?: Subscription;
  private timer?: ReturnType<typeof setTimeout>;
  private dueWhileHidden = false;
  private readonly onVisibility = () => {
    if (!document.hidden && this.dueWhileHidden) {
      this.dueWhileHidden = false;
      this.load$.next();
    }
  };

  get drillable(): boolean {
    return this.tile.drillAction === DashboardDrillAction.FilterDashboard
      || this.tile.drillAction === DashboardDrillAction.OpenReport
      || this.tile.drillAction === DashboardDrillAction.ShowRows;
  }

  toneClass(column: string, value: unknown): string | null {
    const rule = matchRule(this.tile.conditionalRules ?? [], column, value);
    return rule ? 'tone-' + rule.tone : null;
  }

  /**
   * Runs the tile's query in full, downloads it through the query export endpoint — which applies
   * the export gates again, server-side — and releases the cached rows.
   */
  download(format: ExportFormatOption): void {
    this.downloading = true;
    this.cdr.detectChanges();

    this.dashboards.runTileRows(this.dashboardId, this.tile.id, this.filters).pipe(
      switchMap(rows => this.queries.exportJob(rows.jobId, format.apiValue).pipe(
        map(blob => ({ blob, jobId: rows.jobId })),
        catchError(err => {
          this.queries.releaseJob(rows.jobId).subscribe({ error: () => { /* best effort */ } });
          throw err;
        })
      ))
    ).subscribe({
      next: ({ blob, jobId }) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${this.tile.title || 'tile'}_${toIsoDate(new Date())}.${format.apiValue}`;
        a.click();
        window.URL.revokeObjectURL(url);
        this.queries.releaseJob(jobId).subscribe({ error: () => { /* best effort */ } });
        this.downloading = false;
        this.cdr.detectChanges();
      },
      error: err => {
        this.downloading = false;
        this.toast.error(err, 'user.dashboards.downloadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  ngOnInit(): void {
    document.addEventListener('visibilitychange', this.onVisibility);

    this.downloadFormats = this.auth.has(PERM.queriesRun)
      ? EXPORT_FORMATS.filter(f =>
          (this.tile.allowedExportFormats ?? []).includes(f.name) && this.auth.has(f.permission))
      : [];

    // switchMap: a filter change while a fetch is in flight abandons the old one, so a slow
    // answer for the previous filters can never overwrite the answer for the current ones.
    this.subscription = this.load$.pipe(
      switchMap(() => {
        this.loading = true;
        this.cdr.detectChanges();
        return this.dashboards.getTileData(this.dashboardId, this.tile.id, this.filters).pipe(
          map(data => ({ data, error: null as string | null })),
          catchError(err => of({
            data: null,
            error: extractApiError(err, this.transloco.translate('user.dashboards.tileFailed'))
          }))
        );
      })
    ).subscribe(({ data, error }) => {
      this.loading = false;
      if (data) {
        this.data = data;
        this.stale = false;
      } else {
        // Keep showing the last good result and say it is out of date, rather than blanking a
        // wall screen over one dropped request.
        this.stale = this.data !== null;
        this.staleError = error ?? '';
        if (!this.data) {
          this.data = {
            tileId: this.tile.id, generatedAt: new Date().toISOString(),
            nextRefreshAt: new Date().toISOString(), isEmpty: false, error
          };
        }
      }
      this.schedule(data);
      this.cdr.detectChanges();
    });

    this.load$.next();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const refetch = (changes['filters'] && !changes['filters'].firstChange)
      || (changes['refreshNonce'] && !changes['refreshNonce'].firstChange);
    if (refetch) {
      clearTimeout(this.timer);
      this.load$.next();
    }
  }

  ngOnDestroy(): void {
    clearTimeout(this.timer);
    document.removeEventListener('visibilitychange', this.onVisibility);
    this.subscription?.unsubscribe();
  }

  private schedule(data: DashboardTileData | null): void {
    clearTimeout(this.timer);

    const interval = this.tile.effectiveRefreshSeconds * 1000;
    // Clamped so a clock that disagrees with the server's can neither hammer it nor stall.
    const untilFresh = data ? Date.parse(data.nextRefreshAt) - Date.now() : interval;
    const delay = Math.min(Math.max(untilFresh, 5000), interval) + Math.random() * 2000;

    this.timer = setTimeout(() => {
      if (document.hidden) {
        this.dueWhileHidden = true;
      } else {
        this.load$.next();
      }
    }, delay);
  }
}
