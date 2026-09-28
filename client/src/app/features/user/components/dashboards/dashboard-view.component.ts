import { Component, OnDestroy, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { Subscription, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { TranslocoService } from '@jsverse/transloco';
import { DashboardService } from '@core/services/dashboard.service';
import { ToastService, extractApiError } from '@core/services/toast.service';
import { DropdownOption, ParameterType } from '@core/models/dynamic-query.model';
import {
  DATE_PRESETS, datePresetLabelKey, isDatePreset, resolveDatePreset
} from '@core/models/date-presets';
import { fromIsoDate, toIsoDate } from '@core/models/local-date';
import {
  Dashboard, DashboardDrillAction, DashboardFilter, DashboardTile
} from '@core/models/dashboard.model';
import {
  DashboardRowsDialogComponent, DashboardRowsDialogData
} from './dashboard-rows-dialog.component';

/** Query parameters that drive the page itself rather than naming a filter. */
const RESERVED_PARAMS = new Set(['tv', 'rotate', 'every']);

/**
 * A dashboard: the filter bar, then a 12-column grid of tiles that each keep themselves current.
 *
 * <p>The applied filter values live in the URL, so a filtered view can be bookmarked or sent to
 * someone, and a wall screen can be pointed at one. <c>?tv=1</c> is kiosk mode — no app bar, no
 * filter form, larger type — and <c>&rotate=id,id&every=60</c> cycles it through several
 * dashboards.</p>
 */
@Component({
  selector: 'app-dashboard-view',
  standalone: false,
  template: `
    <div class="dashboard" [class.tv]="tvMode">
      <div class="page-header">
        <h1 dir="auto">{{ dashboard?.name || ('user.dashboards.title' | transloco) }}</h1>
        <span class="spacer"></span>
        <div class="header-actions" *ngIf="dashboard">
          <button mat-stroked-button type="button" (click)="refreshAll()"
                  [matTooltip]="'user.dashboards.refreshAllHint' | transloco">
            <mat-icon>refresh</mat-icon> {{ 'user.dashboards.refreshAll' | transloco }}
          </button>
          <button mat-icon-button type="button" (click)="toggleTvMode()"
                  [matTooltip]="(tvMode ? 'user.dashboards.exitTv' : 'user.dashboards.tvMode') | transloco"
                  [attr.aria-label]="(tvMode ? 'user.dashboards.exitTv' : 'user.dashboards.tvMode') | transloco">
            <mat-icon>{{ tvMode ? 'close_fullscreen' : 'tv' }}</mat-icon>
          </button>
          <button mat-icon-button type="button" (click)="toggleFullscreen()" *ngIf="fullscreenSupported"
                  [matTooltip]="'user.dashboards.fullscreen' | transloco"
                  [attr.aria-label]="'user.dashboards.fullscreen' | transloco">
            <mat-icon>{{ isFullscreen ? 'fullscreen_exit' : 'fullscreen' }}</mat-icon>
          </button>
        </div>
      </div>

      <div class="loading" *ngIf="loading">
        <mat-progress-spinner mode="indeterminate" diameter="40"></mat-progress-spinner>
      </div>

      <div class="error-block" *ngIf="error">
        <span class="error-text">{{ error }}</span>
      </div>

      <p class="muted" dir="auto" *ngIf="dashboard?.description && !tvMode">{{ dashboard?.description }}</p>

      <!-- Filter bar. Hidden on a wall screen, where nobody is at a keyboard; the applied values
           still show as chips so the screen says what it is showing. -->
      <form class="filters" *ngIf="dashboard && dashboard.filters.length > 0 && !tvMode"
            [formGroup]="form" (ngSubmit)="apply()">
        <ng-container *ngFor="let f of dashboard.filters">
          <mat-form-field appearance="outline" *ngIf="f.parameterType === ParameterType.String">
            <mat-label>{{ f.displayName }}</mat-label>
            <input matInput [formControlName]="f.name" dir="auto" />
          </mat-form-field>

          <mat-form-field appearance="outline" *ngIf="f.parameterType === ParameterType.Number">
            <mat-label>{{ f.displayName }}</mat-label>
            <input matInput type="number" [formControlName]="f.name" />
          </mat-form-field>

          <!-- A date filter is one of two fields: a picked date, or a preset. A preset keeps its token
               in the form and the URL, so it keeps rolling; that field shows what it means today
               and clears back to a picked date. Two whole fields rather than one field with swapped
               contents, because a form field's control and suffixes are fixed when it is created. -->
          <mat-form-field appearance="outline" *ngIf="f.parameterType === ParameterType.Date && !presetOf(f.name)">
            <mat-label>{{ f.displayName }}</mat-label>
            <input matInput [matDatepicker]="picker" [formControlName]="f.name" />
            <span matSuffix class="date-suffix">
              <mat-datepicker-toggle [for]="picker"></mat-datepicker-toggle>
              <button mat-icon-button type="button" [matMenuTriggerFor]="presetMenu"
                      [matTooltip]="'user.dashboards.presetsHint' | transloco"
                      [attr.aria-label]="'user.dashboards.presetsHint' | transloco">
                <mat-icon>event_repeat</mat-icon>
              </button>
            </span>
            <mat-datepicker #picker></mat-datepicker>
          </mat-form-field>

          <mat-form-field appearance="outline" *ngIf="f.parameterType === ParameterType.Date && presetOf(f.name) as token">
            <mat-label>{{ f.displayName }}</mat-label>
            <input matInput readonly [value]="presetText(token)" [matTooltip]="presetText(token)" />
            <span matSuffix class="date-suffix">
              <button mat-icon-button type="button" [matMenuTriggerFor]="presetMenu"
                      [matTooltip]="'user.dashboards.presetsHint' | transloco"
                      [attr.aria-label]="'user.dashboards.presetsHint' | transloco">
                <mat-icon>event_repeat</mat-icon>
              </button>
              <button mat-icon-button type="button" (click)="clearPreset(f.name)"
                      [matTooltip]="'user.dashboards.clearPreset' | transloco"
                      [attr.aria-label]="'user.dashboards.clearPreset' | transloco">
                <mat-icon>close</mat-icon>
              </button>
            </span>
          </mat-form-field>

          <mat-menu #presetMenu="matMenu">
            <button mat-menu-item *ngFor="let p of datePresets" (click)="setPreset(f.name, p.token)">
              {{ p.labelKey | transloco }}
            </button>
          </mat-menu>

          <div class="toggle-field" *ngIf="f.parameterType === ParameterType.Boolean">
            <mat-slide-toggle [formControlName]="f.name">{{ f.displayName }}</mat-slide-toggle>
          </div>

          <mat-form-field appearance="outline" *ngIf="f.parameterType === ParameterType.Dropdown">
            <mat-label>{{ f.displayName }}</mat-label>
            <mat-select [formControlName]="f.name" [multiple]="f.allowMultiple">
              <mat-option *ngIf="!f.allowMultiple && !f.isRequired" [value]="''">
                {{ 'common.all' | transloco }}
              </mat-option>
              <mat-option *ngFor="let opt of options[f.name]" [value]="opt.value">
                <span dir="auto">{{ opt.label }}</span>
              </mat-option>
            </mat-select>
          </mat-form-field>
        </ng-container>

        <div class="filter-actions">
          <button mat-flat-button color="primary" type="submit">
            <mat-icon>filter_alt</mat-icon> {{ 'user.dashboards.apply' | transloco }}
          </button>
          <button mat-button type="button" (click)="reset()">
            {{ 'user.dashboards.reset' | transloco }}
          </button>
        </div>
      </form>

      <mat-chip-set class="applied" *ngIf="tvMode && appliedChips.length > 0"
                    [attr.aria-label]="'user.dashboards.appliedFilters' | transloco">
        <mat-chip *ngFor="let chip of appliedChips" disabled>
          <span dir="auto">{{ chip }}</span>
        </mat-chip>
      </mat-chip-set>

      <div class="empty muted" *ngIf="dashboard && dashboard.tiles.length === 0">
        {{ 'user.dashboards.noTiles' | transloco }}
      </div>

      <div class="grid" *ngIf="dashboard">
        <app-dashboard-tile *ngFor="let tile of dashboard.tiles; trackBy: trackTile"
                            [dashboardId]="dashboard.id" [tile]="tile"
                            [filters]="applied" [refreshNonce]="refreshNonce"
                            [attr.data-w]="tile.width" [attr.data-h]="tile.height"
                            (drill)="onDrill(tile, $event)">
        </app-dashboard-tile>
      </div>
    </div>
  `,
  styles: [`
    .dashboard { padding: 16px 20px 24px; }
    .header-actions { display: flex; align-items: center; gap: 4px; }
    .muted { color: var(--text-secondary); }
    .filters {
      display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
      gap: 0 12px; align-items: start; margin-block-end: 8px;
    }
    .toggle-field { display: flex; align-items: center; min-block-size: 56px; }
    /* Two icon buttons side by side in one suffix: the picker (or clear) and the presets menu. */
    .date-suffix { display: inline-flex; align-items: center; margin-inline-end: 4px; }
    .filter-actions { display: flex; align-items: center; gap: 8px; min-block-size: 56px; }
    .applied { display: block; margin-block-end: 12px; }
    .empty { padding: 32px; text-align: center; }

    /* The tile grid: twelve equal columns, each tile spanning its width and height. */
    .grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      grid-auto-rows: 170px;
      gap: 14px;
    }
    .grid > [data-w="3"] { grid-column: span 3; }
    .grid > [data-w="4"] { grid-column: span 4; }
    .grid > [data-w="6"] { grid-column: span 6; }
    .grid > [data-w="8"] { grid-column: span 8; }
    .grid > [data-w="12"] { grid-column: span 12; }
    .grid > [data-h="2"] { grid-row: span 2; }
    .grid > [data-h="3"] { grid-row: span 3; }
    /* Narrow screens: quarter- and third-width tiles pair up, then everything stacks. */
    @media (max-width: 1100px) {
      .grid > [data-w="3"], .grid > [data-w="4"] { grid-column: span 6; }
      .grid > [data-w="8"] { grid-column: span 12; }
    }
    @media (max-width: 700px) {
      /* Same specificity as the width rules above, so this one — later — wins. */
      .grid > [data-w] { grid-column: 1 / -1; }
    }

    /* Wall-screen mode: more room per tile, larger headings, readable from across a room. */
    .dashboard.tv { padding: 20px 28px; }
    .dashboard.tv h1 { font-size: 30px; }
    .dashboard.tv .grid { grid-auto-rows: minmax(200px, 22vh); gap: 18px; }
  `]
})
export class DashboardViewComponent implements OnInit, OnDestroy {
  readonly ParameterType = ParameterType;
  readonly datePresets = DATE_PRESETS;

  private dashboards = inject(DashboardService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private dialog = inject(MatDialog);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private cdr = inject(ChangeDetectorRef);

  dashboard: Dashboard | null = null;
  form: FormGroup = this.fb.group({});
  options: Record<string, DropdownOption[]> = {};
  /** The filter values the tiles are showing, in the wire format. */
  applied: Record<string, string> = {};
  appliedChips: string[] = [];
  refreshNonce = 0;
  loading = true;
  error = '';

  tvMode = false;
  isFullscreen = false;
  readonly fullscreenSupported = typeof document !== 'undefined' && !!document.documentElement.requestFullscreen;

  private dashboardId = '';
  private subscriptions = new Subscription();
  private rotateTimer?: ReturnType<typeof setTimeout>;
  private readonly onFullscreenChange = () => {
    this.isFullscreen = !!document.fullscreenElement;
    this.cdr.detectChanges();
  };

  ngOnInit(): void {
    document.addEventListener('fullscreenchange', this.onFullscreenChange);

    // Subscribed rather than read once: rotation navigates between dashboards on this same
    // route, which reuses the component.
    this.subscriptions.add(this.route.paramMap.subscribe(params => {
      const id = params.get('id') ?? '';
      if (id !== this.dashboardId) {
        this.dashboardId = id;
        this.load();
      }
    }));

    this.subscriptions.add(this.route.queryParamMap.subscribe(query => {
      this.setTvMode(query.get('tv') === '1');
      this.scheduleRotation(query);
      if (this.dashboard) {
        this.readAppliedFromUrl(query);
      }
    }));
  }

  ngOnDestroy(): void {
    clearTimeout(this.rotateTimer);
    document.removeEventListener('fullscreenchange', this.onFullscreenChange);
    this.setTvMode(false);
    this.subscriptions.unsubscribe();
  }

  trackTile = (_: number, tile: DashboardTile) => tile.id;

  private load(): void {
    this.loading = true;
    this.error = '';
    this.dashboard = null;
    this.cdr.detectChanges();

    this.dashboards.getMyDashboard(this.dashboardId).subscribe({
      next: dashboard => {
        this.dashboard = {
          ...dashboard,
          tiles: [...dashboard.tiles].sort((a, b) => a.sortOrder - b.sortOrder),
          filters: [...dashboard.filters].sort((a, b) => a.sortOrder - b.sortOrder)
        };
        this.buildForm(this.dashboard.filters);
        this.readAppliedFromUrl(this.route.snapshot.queryParamMap);
        this.loadOptions(this.dashboard.filters);
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

  private buildForm(filters: DashboardFilter[]): void {
    this.form = this.fb.group({});
    for (const f of filters) {
      this.form.addControl(f.name, this.fb.control(
        this.toControlValue(f, f.defaultValue ?? ''),
        f.isRequired ? [Validators.required] : []));
    }
  }

  private loadOptions(filters: DashboardFilter[]): void {
    const dropdowns = filters.filter(f => f.parameterType === ParameterType.Dropdown);
    if (dropdowns.length === 0) return;

    forkJoin(dropdowns.map(f => this.dashboards.getFilterOptions(this.dashboardId, f.id).pipe(
      catchError(() => of([] as DropdownOption[]))
    ))).subscribe(results => {
      dropdowns.forEach((f, i) => this.options[f.name] = results[i]);
      // The chips name options by label, which only now are known.
      this.appliedChips = this.describeApplied();
      this.cdr.detectChanges();
    });
  }

  /** Seeds the form from the URL, falling back to each filter's default, then applies it. */
  private readAppliedFromUrl(query: ParamMap): void {
    if (!this.dashboard) return;
    for (const f of this.dashboard.filters) {
      const fromUrl = query.get(f.name);
      this.form.get(f.name)?.setValue(this.toControlValue(f, fromUrl ?? f.defaultValue ?? ''), { emitEvent: false });
    }
    this.applied = this.toWire();
    this.appliedChips = this.describeApplied();
    this.cdr.detectChanges();
  }

  apply(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toast.error(null, 'user.dashboards.fillRequired');
      return;
    }
    // Written to the URL rather than applied directly: the query-param subscription then applies
    // it, so a typed-in URL and a clicked Apply take exactly the same path.
    const queryParams: Record<string, string | null> = {};
    const wire = this.toWire();
    for (const f of this.dashboard?.filters ?? []) {
      queryParams[f.name] = wire[f.name] ?? null;
    }
    this.router.navigate([], { relativeTo: this.route, queryParams, queryParamsHandling: 'merge', replaceUrl: true });
  }

  reset(): void {
    for (const f of this.dashboard?.filters ?? []) {
      this.form.get(f.name)?.setValue(this.toControlValue(f, f.defaultValue ?? ''));
    }
    this.apply();
  }

  refreshAll(): void {
    this.refreshNonce++;
  }

  onDrill(tile: DashboardTile, category: string | null): void {
    // No category: the tile menu's "Show rows", which every tile offers.
    if (category === null) {
      this.openRows(tile);
      return;
    }

    switch (tile.drillAction) {
      case DashboardDrillAction.FilterDashboard: {
        const filter = this.dashboard?.filters.find(f => f.name === tile.drillFilterName);
        if (!filter) return;
        this.form.get(filter.name)?.setValue(filter.allowMultiple ? [category] : category);
        this.apply();
        return;
      }
      case DashboardDrillAction.OpenReport: {
        if (!tile.drillReportId) return;
        const queryParams = tile.drillReportParameter ? { [tile.drillReportParameter]: category } : {};
        this.router.navigate(['/user/reports', tile.drillReportId, 'view'], { queryParams });
        return;
      }
      case DashboardDrillAction.ShowRows:
        this.openRows(tile);
        return;
    }
  }

  private openRows(tile: DashboardTile): void {
    const data: DashboardRowsDialogData = {
      dashboardId: this.dashboardId, tileId: tile.id, title: tile.title, filters: this.applied
    };
    this.dialog.open(DashboardRowsDialogComponent, {
      data, width: '1000px', maxWidth: '95vw', autoFocus: 'dialog', ariaModal: true
    });
  }

  // ---------------------------------------------------------------- date presets

  presetOf(name: string): string | null {
    const value = this.form.get(name)?.value;
    return isDatePreset(value) ? value as string : null;
  }

  /** "This month — 1 Sep 2026": the preset's name and the date it means today. */
  presetText(token: string): string {
    const key = datePresetLabelKey(token);
    const offset = /^today([+-])(\d+)$/.exec(token.trim().toLowerCase());
    const label = key
      ? this.transloco.translate(key)
      : offset
        ? this.transloco.translate(offset[1] === '-' ? 'user.dashboards.presets.daysAgo' : 'user.dashboards.presets.daysAhead',
            { days: Number(offset[2]) })
        : token;
    const date = resolveDatePreset(token);
    return date ? `${label} — ${date.toLocaleDateString()}` : label;
  }

  setPreset(name: string, token: string): void {
    this.form.get(name)?.setValue(token);
    this.cdr.detectChanges();
  }

  clearPreset(name: string): void {
    const token = this.presetOf(name);
    // Clearing keeps the date the preset meant, as a fixed date the viewer can then adjust.
    this.form.get(name)?.setValue(token ? resolveDatePreset(token) : null);
    this.cdr.detectChanges();
  }

  toggleTvMode(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tv: this.tvMode ? null : '1' },
      queryParamsHandling: 'merge'
    });
  }

  toggleFullscreen(): void {
    if (document.fullscreenElement) {
      document.exitFullscreen().catch(() => { /* already left */ });
    } else {
      document.documentElement.requestFullscreen().catch(() => {
        this.toast.error(null, 'user.dashboards.fullscreenFailed');
      });
    }
  }

  /** Kiosk mode hides the app bar, which lives outside this component — hence a body class. */
  private setTvMode(on: boolean): void {
    this.tvMode = on;
    document.body.classList.toggle('dashboard-kiosk', on);
  }

  /** <c>?rotate=id,id&every=60</c>: after <c>every</c> seconds, move on to the next dashboard. */
  private scheduleRotation(query: ParamMap): void {
    clearTimeout(this.rotateTimer);
    const ids = (query.get('rotate') ?? '').split(',').map(s => s.trim()).filter(Boolean);
    if (ids.length === 0) return;

    const seconds = Math.max(15, Number(query.get('every')) || 60);
    const next = ids[(ids.indexOf(this.dashboardId) + 1) % ids.length];
    if (!next || next === this.dashboardId) return;

    this.rotateTimer = setTimeout(() => {
      // Only the page's own parameters travel with the rotation: another dashboard's filters
      // are its own, and carrying this one's across would mis-filter it.
      const keep: Record<string, string> = {};
      for (const key of RESERVED_PARAMS) {
        const value = query.get(key);
        if (value !== null) keep[key] = value;
      }
      this.router.navigate(['/user/dashboards', next], { queryParams: keep });
    }, seconds * 1000);
  }

  // ---------------------------------------------------------------- value conversion

  /** A stored or URL value, turned into what the filter's form control holds. */
  private toControlValue(f: DashboardFilter, raw: string): unknown {
    switch (f.parameterType) {
      case ParameterType.Boolean:
        return raw === 'true';
      case ParameterType.Date: {
        if (!raw) return null;
        if (isDatePreset(raw)) return raw.trim().toLowerCase();
        return fromIsoDate(raw);
      }
      case ParameterType.Dropdown:
        if (!f.allowMultiple) return raw;
        try {
          const parsed = raw ? JSON.parse(raw) : [];
          return Array.isArray(parsed) ? parsed.map(String) : [];
        } catch {
          return [];
        }
      default:
        return raw;
    }
  }

  /**
   * The form as the API's wire format: plain strings, dates as yyyy-mm-dd, multi-selects as a
   * JSON-array string. Empty values are left out, so the server falls back to the default.
   */
  private toWire(): Record<string, string> {
    const wire: Record<string, string> = {};
    for (const f of this.dashboard?.filters ?? []) {
      const value = this.form.get(f.name)?.value;
      let text: string;

      if (f.parameterType === ParameterType.Date && isDatePreset(value)) {
        text = value as string;
      } else if (f.parameterType === ParameterType.Date) {
        text = value instanceof Date ? toIsoDate(value) : '';
      } else if (f.parameterType === ParameterType.Boolean) {
        text = String(!!value);
      } else if (f.parameterType === ParameterType.Dropdown && f.allowMultiple) {
        text = Array.isArray(value) && value.length > 0 ? JSON.stringify(value) : '';
      } else {
        text = value === null || value === undefined ? '' : String(value);
      }

      if (text !== '') wire[f.name] = text;
    }
    return wire;
  }

  private describeApplied(): string[] {
    const chips: string[] = [];
    for (const f of this.dashboard?.filters ?? []) {
      const value = this.form.get(f.name)?.value;
      if (value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0)) continue;

      let text: string;
      if (f.parameterType === ParameterType.Date && isDatePreset(value)) {
        text = this.presetText(value as string);
      } else if (value instanceof Date) {
        text = value.toLocaleDateString();
      } else if (Array.isArray(value)) {
        const labels = this.options[f.name];
        const separator = this.transloco.getActiveLang() === 'ar' ? '، ' : ', ';
        text = value.map(v => labels?.find(o => o.value === v)?.label ?? v).join(separator);
      } else {
        text = String(value);
      }
      chips.push(`${f.displayName}: ${text}`);
    }
    return chips;
  }
}
