import { Component, Input, OnChanges, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import {
  DashboardConditionalRule, DashboardKpiData, DashboardValueFormat, matchRule
} from '@core/models/dashboard.model';

/**
 * A dashboard KPI: one headline number, its change from a comparison value, and a sparkline of
 * the series it came from.
 *
 * <p>The change is coloured by whether it is good news, not by its sign — {@link higherIsBetter}
 * says which way that is, because complaints falling is a green arrow pointing down. The arrow
 * and the wording carry the direction too, so the colour is never the only signal.</p>
 *
 * <p>With a target it also says met, near or missed — in words as well as colour — with a bar
 * showing how far along the value is. A colour rule, when one matches, tints the number itself.</p>
 */
@Component({
  selector: 'app-kpi-tile',
  standalone: true,
  imports: [CommonModule, MatIconModule, TranslocoModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="kpi">
      <div class="value" dir="ltr" [ngClass]="valueTone ? 'tone-' + valueTone : ''">{{ valueText }}</div>

      <div class="delta" *ngIf="kpi.deltaPercent !== null"
           [class.good]="tone === 'good'" [class.bad]="tone === 'bad'">
        <mat-icon aria-hidden="true">{{ icon }}</mat-icon>
        <span>
          {{ (kpi.deltaPercent > 0 ? 'user.dashboards.kpiUp' : kpi.deltaPercent < 0 ? 'user.dashboards.kpiDown' : 'user.dashboards.kpiFlat')
             | transloco: { percent: deltaText, compare: compareText } }}
        </span>
      </div>

      <div class="target" *ngIf="kpi.target !== null && kpi.target !== undefined"
           [ngClass]="kpi.targetStatus ? 'status-' + kpi.targetStatus : ''">
        <div class="target-text">
          <span>{{ 'user.dashboards.kpiTarget' | transloco: { target: targetText } }}</span>
          <span class="target-status" *ngIf="kpi.targetStatus">
            {{ ('user.dashboards.target.' + kpi.targetStatus) | transloco }}
          </span>
        </div>
        <div class="target-bar" role="progressbar" [attr.aria-valuenow]="progress" aria-valuemin="0"
             aria-valuemax="100" [attr.aria-label]="'user.dashboards.kpiTarget' | transloco: { target: targetText }">
          <span [style.inline-size.%]="progress"></span>
        </div>
      </div>

      <svg class="spark" *ngIf="sparkPoints" viewBox="0 0 100 30" preserveAspectRatio="none"
           aria-hidden="true">
        <polyline [attr.points]="sparkPoints" fill="none" vector-effect="non-scaling-stroke" />
      </svg>
    </div>
  `,
  styles: [`
    :host { display: block; block-size: 100%; }
    .kpi { display: flex; flex-direction: column; justify-content: center; block-size: 100%; gap: 6px; }
    .value {
      font-size: clamp(28px, 4vw, 44px); font-weight: 600; line-height: 1.1;
      color: var(--text-primary); font-variant-numeric: tabular-nums;
      /* An LTR run inside an RTL page, aligned to the page's start edge. */
      text-align: start;
    }
    .delta { display: flex; align-items: center; gap: 4px; font-size: 13px; color: var(--text-secondary); }
    .delta mat-icon { font-size: 18px; inline-size: 18px; block-size: 18px; }
    .delta.good { color: var(--status-success); }
    .delta.bad { color: var(--status-error); }
    .tone-good { color: var(--status-success); }
    .tone-warn { color: var(--status-warning); }
    .tone-bad { color: var(--status-error); }
    .target { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: var(--text-secondary); }
    .target-text { display: flex; justify-content: space-between; gap: 8px; }
    .target-status { font-weight: 600; }
    .target-bar { block-size: 6px; border-radius: 3px; background: var(--divider-color); overflow: hidden; }
    .target-bar span { display: block; block-size: 100%; border-radius: 3px; background: var(--text-secondary); }
    .status-met .target-status { color: var(--status-success); }
    .status-met .target-bar span { background: var(--status-success); }
    .status-near .target-status { color: var(--status-warning); }
    .status-near .target-bar span { background: var(--status-warning); }
    .status-missed .target-status { color: var(--status-error); }
    .status-missed .target-bar span { background: var(--status-error); }
    .spark { inline-size: 100%; block-size: 36px; margin-block-start: 4px; }
    .spark polyline { stroke: var(--accent-primary); stroke-width: 2; }
  `]
})
export class KpiTileComponent implements OnChanges {
  @Input({ required: true }) kpi!: DashboardKpiData;
  @Input() format: DashboardValueFormat = DashboardValueFormat.Number;
  @Input() higherIsBetter = true;
  /** Colour rules; only those with no column (the KPI's own value) apply here. */
  @Input() rules: DashboardConditionalRule[] = [];

  valueText = '';
  deltaText = '';
  compareText = '';
  icon = 'trending_flat';
  tone: 'good' | 'bad' | 'neutral' = 'neutral';
  sparkPoints: string | null = null;
  targetText = '';
  /** How far the value is towards the target, 0–100, for the bar. */
  progress = 0;
  valueTone: string | null = null;

  ngOnChanges(): void {
    this.valueText = this.kpi.value === null ? '—' : this.formatValue(this.kpi.value);
    this.compareText = this.kpi.compare === null ? '' : this.formatValue(this.kpi.compare);

    const delta = this.kpi.deltaPercent;
    if (delta === null) {
      this.deltaText = '';
      this.tone = 'neutral';
    } else {
      this.deltaText = Math.abs(delta).toLocaleString(undefined, { maximumFractionDigits: 1 }) + '%';
      this.icon = delta > 0 ? 'trending_up' : delta < 0 ? 'trending_down' : 'trending_flat';
      this.tone = delta === 0 ? 'neutral' : (delta > 0) === this.higherIsBetter ? 'good' : 'bad';
    }

    this.sparkPoints = this.buildSparkline(this.kpi.sparkline);

    const target = this.kpi.target;
    this.targetText = target === null || target === undefined ? '' : this.formatValue(target);
    this.progress = target && this.kpi.value !== null
      ? Math.max(0, Math.min(100, (this.kpi.value / target) * 100))
      : 0;
    this.valueTone = matchRule(this.rules, null, this.kpi.value)?.tone ?? null;
  }

  private formatValue(value: number): string {
    switch (this.format) {
      case DashboardValueFormat.Percent:
        return value.toLocaleString(undefined, { style: 'percent', maximumFractionDigits: 1 });
      case DashboardValueFormat.Currency:
        // No currency is configured anywhere in the system, so "currency" means money-shaped:
        // always two decimals, grouped.
        return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
      default:
        return value.toLocaleString(undefined, { maximumFractionDigits: 2 });
    }
  }

  /** Scaled to its own range, not from zero: a sparkline is about shape, not magnitude. */
  private buildSparkline(values: (number | null)[]): string | null {
    const points = values
      .map((v, i) => ({ v, i }))
      .filter((p): p is { v: number; i: number } => p.v !== null);
    if (points.length < 2) return null;

    const min = Math.min(...points.map(p => p.v));
    const max = Math.max(...points.map(p => p.v));
    const span = max - min || 1;
    const step = 100 / Math.max(1, values.length - 1);

    return points
      .map(p => `${(p.i * step).toFixed(2)},${(28 - ((p.v - min) / span) * 26).toFixed(2)}`)
      .join(' ');
  }
}
