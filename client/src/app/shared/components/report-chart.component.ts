import { Component, Input, OnChanges, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReportRunChart, ReportChartType } from '@core/models/report.model';

interface Bar { x: number; y: number; w: number; h: number; fill: string; label: string; value: string; }
interface Slice { path: string; fill: string; label: string; value: string; percent: string; }
interface Line { points: string; stroke: string; name: string; dots: { cx: number; cy: number }[]; }
interface Tick { y: number; label: string; }

/**
 * Draws a report chart as inline SVG.
 *
 * <p>Hand-rolled because the offline bundle admits no chart library. It reads the same
 * categories and series the server built for the Word document, so the figure on screen and the
 * figure in the exported file are the same figure — not two renderings that can drift.</p>
 *
 * <p>Geometry is computed once in {@link ngOnChanges} rather than in the template: expressions
 * in an *ngFor re-evaluate on every change-detection pass, and this component sits inside a
 * page that polls.</p>
 */
@Component({
  selector: 'app-report-chart',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="chart" *ngIf="chart">
      <figcaption *ngIf="chart.title" dir="auto">{{ chart.title }}</figcaption>

      <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" role="img"
           [attr.aria-label]="chart.title || 'chart'" preserveAspectRatio="xMidYMid meet">

        <!-- value axis gridlines, skipped for a pie -->
        <ng-container *ngIf="!isPie">
          <g *ngFor="let t of ticks">
            <line [attr.x1]="padLeft" [attr.x2]="width - padRight" [attr.y1]="t.y" [attr.y2]="t.y"
                  class="grid" />
            <text [attr.x]="padLeft - 6" [attr.y]="t.y + 4" class="tick" text-anchor="end">
              {{ t.label }}
            </text>
          </g>
          <line [attr.x1]="padLeft" [attr.x2]="padLeft"
                [attr.y1]="padTop" [attr.y2]="height - padBottom" class="axis" />
          <line [attr.x1]="padLeft" [attr.x2]="width - padRight"
                [attr.y1]="height - padBottom" [attr.y2]="height - padBottom" class="axis" />
        </ng-container>

        <!-- bars -->
        <g *ngFor="let b of bars">
          <rect [attr.x]="b.x" [attr.y]="b.y" [attr.width]="b.w" [attr.height]="b.h"
                [attr.fill]="b.fill" rx="2">
            <title>{{ b.label }}: {{ b.value }}</title>
          </rect>
        </g>

        <!-- lines -->
        <g *ngFor="let l of lines">
          <polyline [attr.points]="l.points" [attr.stroke]="l.stroke" fill="none" stroke-width="2" />
          <circle *ngFor="let d of l.dots" [attr.cx]="d.cx" [attr.cy]="d.cy" r="3"
                  [attr.fill]="l.stroke" />
        </g>

        <!-- pie -->
        <g *ngFor="let s of slices">
          <path [attr.d]="s.path" [attr.fill]="s.fill">
            <title>{{ s.label }}: {{ s.value }} ({{ s.percent }})</title>
          </path>
        </g>

        <!-- category labels -->
        <g *ngIf="!isPie">
          <text *ngFor="let c of categoryLabels; let i = index"
                [attr.x]="c.x" [attr.y]="height - padBottom + 16"
                class="tick" text-anchor="middle" dir="auto">{{ c.text }}</text>
        </g>
      </svg>

      <ul class="legend" *ngIf="legend.length > 1 || isPie">
        <li *ngFor="let item of legend">
          <span class="swatch" [style.background]="item.color"></span>
          <span dir="auto">{{ item.name }}</span>
        </li>
      </ul>

      <!-- The same numbers as a table, for anyone using a screen reader. -->
      <table class="sr-only">
        <caption>{{ chart.title }}</caption>
        <tr><th>&nbsp;</th><th *ngFor="let s of chart.series">{{ s.name }}</th></tr>
        <tr *ngFor="let cat of chart.categories; let i = index">
          <th>{{ cat }}</th>
          <td *ngFor="let s of chart.series">{{ s.values[i] }}</td>
        </tr>
      </table>
    </figure>
  `,
  styles: [`
    .chart { margin: 0 0 20px; }
    figcaption { font-weight: 600; margin-block-end: 8px; color: var(--text-primary); }
    svg { inline-size: 100%; block-size: auto; max-block-size: 340px; }
    .grid { stroke: var(--divider-color); stroke-width: 1; }
    .axis { stroke: var(--border-color); stroke-width: 1; }
    .tick { font-size: 11px; fill: var(--text-secondary); }
    .legend { display: flex; flex-wrap: wrap; gap: 12px; list-style: none; padding: 0; margin: 8px 0 0; }
    .legend li { display: flex; align-items: center; gap: 6px; font-size: 12px; color: var(--text-secondary); }
    .swatch { inline-size: 12px; block-size: 12px; border-radius: 2px; }
    .sr-only {
      position: absolute; inline-size: 1px; block-size: 1px;
      overflow: hidden; clip-path: inset(50%); white-space: nowrap;
    }
  `]
})
export class ReportChartComponent implements OnChanges {
  @Input({ required: true }) chart!: ReportRunChart;

  readonly width = 640;
  readonly height = 300;
  readonly padLeft = 56;
  readonly padRight = 16;
  readonly padTop = 12;
  readonly padBottom = 34;

  bars: Bar[] = [];
  lines: Line[] = [];
  slices: Slice[] = [];
  ticks: Tick[] = [];
  categoryLabels: { x: number; text: string }[] = [];
  legend: { name: string; color: string }[] = [];
  isPie = false;

  /**
   * Categorical palette. Fixed rather than themed: a series must keep its colour when the
   * viewer switches between light and dark, or the chart appears to change meaning.
   */
  private readonly palette = [
    '#4f46e5', '#0891b2', '#16a34a', '#ca8a04', '#dc2626',
    '#7c3aed', '#0d9488', '#65a30d', '#ea580c', '#be123c'
  ];

  ngOnChanges(): void {
    this.bars = [];
    this.lines = [];
    this.slices = [];
    this.ticks = [];
    this.categoryLabels = [];
    this.legend = [];

    if (!this.chart || this.chart.categories.length === 0 || this.chart.series.length === 0) {
      return;
    }

    this.isPie = this.chart.type === ReportChartType.Pie;
    this.legend = this.isPie
      ? this.chart.categories.map((c, i) => ({ name: c, color: this.colour(i) }))
      : this.chart.series.map((s, i) => ({ name: s.name, color: this.colour(i) }));

    if (this.isPie) {
      this.buildPie();
    } else if (this.chart.type === ReportChartType.Line) {
      this.buildAxes();
      this.buildLines();
    } else {
      this.buildAxes();
      this.buildBars();
    }
  }

  private colour(index: number): string {
    return this.palette[index % this.palette.length];
  }

  /** Highest value across every series; the axis always includes zero. */
  private get maxValue(): number {
    const values = this.chart.series.flatMap(s => s.values).filter(v => v !== null) as number[];
    return values.length === 0 ? 1 : Math.max(1, ...values);
  }

  private buildAxes(): void {
    const max = this.maxValue;
    const plotHeight = this.height - this.padTop - this.padBottom;

    for (let i = 0; i <= 4; i++) {
      const value = (max / 4) * i;
      this.ticks.push({
        y: this.height - this.padBottom - (plotHeight * i) / 4,
        label: this.abbreviate(value)
      });
    }

    const plotWidth = this.width - this.padLeft - this.padRight;
    const step = plotWidth / this.chart.categories.length;

    // Crowded axes become unreadable, so labels thin out rather than overlap.
    const every = Math.ceil(this.chart.categories.length / 8);
    this.chart.categories.forEach((text, i) => {
      if (i % every !== 0) return;
      this.categoryLabels.push({
        x: this.padLeft + step * i + step / 2,
        text: text.length > 12 ? text.slice(0, 11) + '…' : text
      });
    });
  }

  private buildBars(): void {
    const max = this.maxValue;
    const plotWidth = this.width - this.padLeft - this.padRight;
    const plotHeight = this.height - this.padTop - this.padBottom;
    const groupWidth = plotWidth / this.chart.categories.length;
    const barWidth = Math.max(2, (groupWidth * 0.7) / this.chart.series.length);

    this.chart.series.forEach((series, s) => {
      series.values.forEach((value, i) => {
        if (value === null) return;
        const h = (value / max) * plotHeight;
        this.bars.push({
          x: this.padLeft + groupWidth * i + groupWidth * 0.15 + barWidth * s,
          y: this.height - this.padBottom - h,
          w: barWidth,
          h,
          fill: this.colour(s),
          label: this.chart.categories[i],
          value: this.format(value)
        });
      });
    });
  }

  private buildLines(): void {
    const max = this.maxValue;
    const plotWidth = this.width - this.padLeft - this.padRight;
    const plotHeight = this.height - this.padTop - this.padBottom;
    const step = plotWidth / Math.max(1, this.chart.categories.length - 1);

    this.chart.series.forEach((series, s) => {
      const dots: { cx: number; cy: number }[] = [];
      const points: string[] = [];

      series.values.forEach((value, i) => {
        if (value === null) return;   // a gap breaks the line rather than dropping to zero
        const cx = this.padLeft + step * i;
        const cy = this.height - this.padBottom - (value / max) * plotHeight;
        points.push(`${cx},${cy}`);
        dots.push({ cx, cy });
      });

      this.lines.push({ points: points.join(' '), stroke: this.colour(s), name: series.name, dots });
    });
  }

  private buildPie(): void {
    const values = this.chart.series[0].values.map(v => v ?? 0);
    const total = values.reduce((sum, v) => sum + v, 0);
    if (total <= 0) return;

    const cx = this.width / 2;
    const cy = this.height / 2;
    const r = Math.min(this.width, this.height) / 2 - 20;
    let angle = -Math.PI / 2;

    values.forEach((value, i) => {
      const sweep = (value / total) * Math.PI * 2;
      const end = angle + sweep;
      const large = sweep > Math.PI ? 1 : 0;

      const x1 = cx + r * Math.cos(angle);
      const y1 = cy + r * Math.sin(angle);
      const x2 = cx + r * Math.cos(end);
      const y2 = cy + r * Math.sin(end);

      this.slices.push({
        path: `M ${cx} ${cy} L ${x1} ${y1} A ${r} ${r} 0 ${large} 1 ${x2} ${y2} Z`,
        fill: this.colour(i),
        label: this.chart.categories[i],
        value: this.format(value),
        percent: ((value / total) * 100).toFixed(1) + '%'
      });

      angle = end;
    });
  }

  private format(value: number): string {
    return value.toLocaleString(undefined, { maximumFractionDigits: 2 });
  }

  /** Axis labels stay short so they do not eat the plot. */
  private abbreviate(value: number): string {
    if (Math.abs(value) >= 1_000_000) return (value / 1_000_000).toFixed(1) + 'M';
    if (Math.abs(value) >= 1_000) return (value / 1_000).toFixed(0) + 'K';
    return value.toFixed(Math.abs(value) < 10 && value % 1 !== 0 ? 1 : 0);
  }
}
