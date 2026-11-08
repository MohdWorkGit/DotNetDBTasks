import { Component, Input, Output, EventEmitter, OnChanges, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReportRunChart } from '@core/models/report.model';
import { CHART_PALETTE, formatNumber } from './chart-geometry';

interface Stage { label: string; value: string; width: number; share: string; }

/**
 * Stages as centred bars, widest first, each with its value and its share of the first stage —
 * the conversion a funnel exists to show. The server has already sorted the stages and kept the
 * first series only.
 *
 * <p>Plain HTML rather than SVG: the labels are text in the page's own flow, so long Arabic stage
 * names wrap and align as the rest of the page does.</p>
 */
@Component({
  selector: 'app-funnel-chart',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="funnel" [attr.aria-label]="chart.series[0]?.name">
      <li *ngFor="let s of stages" class="stage">
        <span class="label" dir="auto" [title]="s.label">{{ s.label }}</span>
        <span class="track">
          <button type="button" class="bar" [style.inline-size.%]="s.width" [style.background]="colour"
                  [disabled]="!interactive" [attr.aria-label]="s.label + ': ' + s.value + ' (' + s.share + ')'"
                  (click)="categoryClick.emit(s.label)">
          </button>
        </span>
        <span class="figure" dir="ltr">{{ s.value }} <span class="share">{{ s.share }}</span></span>
      </li>
    </ol>
  `,
  styles: [`
    .funnel { list-style: none; margin: 0; padding: 0; block-size: 100%; display: flex; flex-direction: column; justify-content: space-evenly; gap: 2px; }
    .stage { display: grid; grid-template-columns: minmax(60px, 28%) 1fr auto; align-items: center; gap: 8px; min-block-size: 0; flex: 1 1 0; max-block-size: 36px; }
    .label { font-size: 12px; color: var(--text-primary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .track { display: flex; justify-content: center; block-size: 100%; }
    .bar { block-size: 100%; min-block-size: 8px; min-inline-size: 2px; border: 0; border-radius: 4px; padding: 0; cursor: default; }
    .bar:not(:disabled) { cursor: pointer; }
    .bar:not(:disabled):hover, .bar:focus-visible { opacity: 0.8; }
    .bar:focus-visible { outline: 2px solid var(--accent-primary); outline-offset: 2px; }
    .figure { font-size: 12px; color: var(--text-primary); white-space: nowrap; }
    .share { color: var(--text-secondary); margin-inline-start: 4px; }
  `]
})
export class FunnelChartComponent implements OnChanges {
  @Input({ required: true }) chart!: ReportRunChart;
  @Input() interactive = false;
  /** Palette index for the bars. */
  @Input() colourIndex = 0;
  @Output() categoryClick = new EventEmitter<string>();

  stages: Stage[] = [];
  colour = CHART_PALETTE[0];

  ngOnChanges(): void {
    this.colour = CHART_PALETTE[this.colourIndex % CHART_PALETTE.length];
    const values = (this.chart.series[0]?.values ?? []).map(v => Math.max(0, v ?? 0));
    const first = values[0] || 0;
    const max = Math.max(1, ...values);
    this.stages = this.chart.categories.map((label, i) => ({
      label,
      value: formatNumber(values[i]),
      width: (values[i] / max) * 100,
      share: first > 0 ? Math.round((values[i] / first) * 100) + '%' : ''
    }));
  }
}
