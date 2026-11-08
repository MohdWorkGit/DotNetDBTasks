import { Component, Input, OnChanges, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoModule } from '@jsverse/transloco';
import {
  DashboardKpiData, DashboardValueFormat, formatDashboardValue
} from '@core/models/dashboard.model';

/**
 * A KPI as a dial: a half-circle from zero to a maximum, filled to the value, with the target
 * marked on the arc.
 *
 * <p>The fill takes the target status (met / near / missed) from the same server rule a KPI uses,
 * and says it in words beneath, so the colour is never the only signal. Without a target the
 * fill is the accent colour and nothing is judged.</p>
 */
@Component({
  selector: 'app-gauge-chart',
  standalone: true,
  imports: [CommonModule, TranslocoModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="gauge">
      <svg viewBox="0 0 200 120" role="img"
           [attr.aria-label]="valueText + (targetText ? ' / ' + targetText : '')">
        <path [attr.d]="track" class="track" />
        <path *ngIf="fill" [attr.d]="fill" class="fill" [ngClass]="kpi.targetStatus ? 'status-' + kpi.targetStatus : ''" />
        <line *ngIf="targetMark" [attr.x1]="targetMark.x1" [attr.y1]="targetMark.y1"
              [attr.x2]="targetMark.x2" [attr.y2]="targetMark.y2" class="target" />
        <text x="100" y="98" text-anchor="middle" class="value" dir="ltr">{{ valueText }}</text>
        <text x="20" y="116" text-anchor="middle" class="end" dir="ltr">0</text>
        <text x="180" y="116" text-anchor="middle" class="end" dir="ltr">{{ maxText }}</text>
      </svg>
      <div class="caption" *ngIf="targetText"
           [ngClass]="kpi.targetStatus ? 'status-' + kpi.targetStatus : ''">
        {{ 'user.dashboards.kpiTarget' | transloco: { target: targetText } }}
        <ng-container *ngIf="kpi.targetStatus"> · {{ ('user.dashboards.target.' + kpi.targetStatus) | transloco }}</ng-container>
      </div>
    </div>
  `,
  styles: [`
    .gauge { block-size: 100%; display: flex; flex-direction: column; align-items: center; justify-content: center; }
    svg { inline-size: 100%; max-inline-size: 320px; flex: 1 1 auto; min-block-size: 0; }
    .track { fill: none; stroke: var(--divider-color); stroke-width: 16; stroke-linecap: round; }
    .fill { fill: none; stroke: var(--accent-primary); stroke-width: 16; stroke-linecap: round; }
    .fill.status-met { stroke: var(--status-success); }
    .fill.status-near { stroke: var(--status-warning); }
    .fill.status-missed { stroke: var(--status-error); }
    .target { stroke: var(--text-primary); stroke-width: 3; }
    .value { font-size: 26px; font-weight: 600; fill: var(--text-primary); }
    .end { font-size: 10px; fill: var(--text-secondary); }
    .caption { font-size: 12px; color: var(--text-secondary); text-align: center; }
    .caption.status-met { color: var(--status-success); font-weight: 600; }
    .caption.status-near { color: var(--status-warning); font-weight: 600; }
    .caption.status-missed { color: var(--status-error); font-weight: 600; }
  `]
})
export class GaugeChartComponent implements OnChanges {
  @Input({ required: true }) kpi!: DashboardKpiData;
  @Input() format: DashboardValueFormat = DashboardValueFormat.Number;
  /** The end of the dial; null takes the target or the value, with headroom. */
  @Input() max: number | null = null;

  readonly track = this.arc(0, 1);
  fill: string | null = null;
  targetMark: { x1: number; y1: number; x2: number; y2: number } | null = null;
  valueText = '';
  maxText = '';
  targetText = '';

  private static readonly CX = 100;
  private static readonly CY = 100;
  private static readonly R = 78;

  ngOnChanges(): void {
    const value = this.kpi.value;
    const target = this.kpi.target ?? null;
    const max = this.max && this.max > 0
      ? this.max
      : this.niceCeiling(Math.max(target ?? 0, value ?? 0) * 1.2);

    this.valueText = value === null ? '—' : formatDashboardValue(value, this.format);
    this.maxText = formatDashboardValue(max, this.format);
    this.targetText = target === null ? '' : formatDashboardValue(target, this.format);

    const share = value === null ? 0 : Math.max(0, Math.min(1, value / max));
    this.fill = share > 0 ? this.arc(0, share) : null;

    if (target !== null && target >= 0 && target <= max) {
      const a = Math.PI * (1 - target / max);
      const { CX, CY, R } = GaugeChartComponent;
      this.targetMark = {
        x1: CX + (R - 13) * Math.cos(a), y1: CY - (R - 13) * Math.sin(a),
        x2: CX + (R + 13) * Math.cos(a), y2: CY - (R + 13) * Math.sin(a)
      };
    } else {
      this.targetMark = null;
    }
  }

  /** The arc from fraction <c>from</c> to <c>to</c> of the half-circle, left to right. */
  private arc(from: number, to: number): string {
    const { CX, CY, R } = GaugeChartComponent;
    const point = (t: number) => {
      const a = Math.PI * (1 - t);
      return `${CX + R * Math.cos(a)} ${CY - R * Math.sin(a)}`;
    };
    return `M ${point(from)} A ${R} ${R} 0 0 1 ${point(to)}`;
  }

  /** 1, 2 or 5 times a power of ten — a dial ending at 1,000 rather than 1,137. */
  private niceCeiling(value: number): number {
    if (value <= 0) return 1;
    const power = Math.pow(10, Math.floor(Math.log10(value)));
    const step = [1, 2, 5, 10].find(m => m * power >= value) ?? 10;
    return step * power;
  }
}
