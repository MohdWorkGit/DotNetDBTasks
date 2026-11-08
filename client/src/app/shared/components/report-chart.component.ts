import { Component, Input, Output, EventEmitter, OnChanges, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReportRunChart } from '@core/models/report.model';
import { ChartDrawOptions, ChartGeometry, buildChartGeometry } from './chart-geometry';

/**
 * Draws a report or dashboard chart as inline SVG — column, bar, line, area, stacked, pie,
 * doughnut and combo.
 *
 * <p>Hand-rolled because the offline bundle admits no chart library. It reads the same
 * categories and series the server built for the Word document, so the figure on screen and the
 * figure in the exported file are the same figure — not two renderings that can drift. The
 * geometry lives in {@link buildChartGeometry}; this component is only its template.</p>
 *
 * <p>Geometry is computed once in {@link ngOnChanges} rather than in the template: expressions
 * in an *ngFor re-evaluate on every change-detection pass, and this component sits inside a
 * page that polls.</p>
 *
 * <p>On a dashboard it is {@link interactive}: every bar, slice and point becomes a focusable
 * button that emits its category, which is what drill-through hangs off.</p>
 */
@Component({
  selector: 'app-report-chart',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure class="chart" [class.compact]="compact" *ngIf="chart && g">
      <figcaption *ngIf="chart.title" dir="auto">{{ chart.title }}</figcaption>

      <ng-container *ngIf="g.legendPosition === 'top'">
        <ng-container *ngTemplateOutlet="legendTpl"></ng-container>
      </ng-container>

      <svg [attr.viewBox]="'0 0 ' + g.width + ' ' + g.height" role="img"
           [attr.aria-label]="chart.title || 'chart'" preserveAspectRatio="xMidYMid meet">

        <!-- value axis gridlines, skipped for a pie -->
        <ng-container *ngIf="!g.pieLike">
          <ng-container *ngIf="!g.horizontal">
            <g *ngFor="let t of g.valueTicks">
              <line [attr.x1]="g.plot.left" [attr.x2]="g.plot.right" [attr.y1]="t.pos" [attr.y2]="t.pos" class="grid" />
              <text [attr.x]="g.plot.left - 6" [attr.y]="t.pos + 4" class="tick" text-anchor="end">{{ t.label }}</text>
            </g>
            <text *ngFor="let t of g.secondaryTicks" [attr.x]="g.plot.right + 6" [attr.y]="t.pos + 4"
                  class="tick" text-anchor="start">{{ t.label }}</text>
          </ng-container>
          <ng-container *ngIf="g.horizontal">
            <g *ngFor="let t of g.valueTicks">
              <line [attr.x1]="t.pos" [attr.x2]="t.pos" [attr.y1]="g.plot.top" [attr.y2]="g.plot.bottom" class="grid" />
              <text [attr.x]="t.pos" [attr.y]="g.plot.bottom + 16" class="tick" text-anchor="middle">{{ t.label }}</text>
            </g>
          </ng-container>
          <line [attr.x1]="g.plot.left" [attr.x2]="g.plot.left"
                [attr.y1]="g.plot.top" [attr.y2]="g.plot.bottom" class="axis" />
          <line [attr.x1]="g.plot.left" [attr.x2]="g.plot.right"
                [attr.y1]="g.plot.bottom" [attr.y2]="g.plot.bottom" class="axis" />
        </ng-container>

        <!-- areas sit behind everything else -->
        <path *ngFor="let a of g.areas" [attr.d]="a.path" [attr.fill]="a.fill" class="area" />

        <!-- bars -->
        <rect *ngFor="let b of g.bars" [attr.x]="b.x" [attr.y]="b.y" [attr.width]="b.w" [attr.height]="b.h"
              [attr.fill]="b.fill" rx="2" class="mark" [class.clickable]="interactive"
              [attr.tabindex]="interactive ? 0 : null" [attr.role]="interactive ? 'button' : null"
              [attr.aria-label]="interactive ? b.label + ', ' + b.series + ': ' + b.value : null"
              (click)="select(b.label)" (keydown.enter)="select(b.label)" (keydown.space)="select(b.label, $event)">
          <title>{{ b.label }} · {{ b.series }}: {{ b.value }}</title>
        </rect>

        <!-- lines -->
        <g *ngFor="let l of g.lines">
          <polyline *ngFor="let points of l.segments" [attr.points]="points" [attr.stroke]="l.stroke"
                    fill="none" stroke-width="2" stroke-linejoin="round" />
          <circle *ngFor="let d of l.dots" [attr.cx]="d.cx" [attr.cy]="d.cy"
                  [attr.r]="interactive ? 5 : 3" [attr.fill]="l.stroke" class="dot" [class.clickable]="interactive"
                  [attr.tabindex]="interactive ? 0 : null" [attr.role]="interactive ? 'button' : null"
                  [attr.aria-label]="interactive ? d.label + ', ' + l.name + ': ' + d.value : null"
                  (click)="select(d.label)" (keydown.enter)="select(d.label)" (keydown.space)="select(d.label, $event)">
            <title>{{ d.label }} · {{ l.name }}: {{ d.value }}</title>
          </circle>
        </g>

        <!-- pie and doughnut -->
        <path *ngFor="let s of g.slices" [attr.d]="s.path" [attr.fill]="s.fill" class="mark slice" [class.clickable]="interactive"
              [attr.tabindex]="interactive ? 0 : null" [attr.role]="interactive ? 'button' : null"
              [attr.aria-label]="interactive ? s.label + ': ' + s.value + ' (' + s.percent + ')' : null"
              (click)="select(s.label)" (keydown.enter)="select(s.label)" (keydown.space)="select(s.label, $event)">
          <title>{{ s.label }}: {{ s.value }} ({{ s.percent }})</title>
        </path>
        <text *ngIf="g.centre" [attr.x]="g.centre.x" [attr.y]="g.centre.y" class="centre" text-anchor="middle">{{ g.centre.text }}</text>

        <!-- reference line -->
        <g *ngIf="g.reference as r">
          <line [attr.x1]="r.x1" [attr.y1]="r.y1" [attr.x2]="r.x2" [attr.y2]="r.y2" class="reference" />
          <text *ngIf="r.label" [attr.x]="r.label.x" [attr.y]="r.label.y" [attr.text-anchor]="r.label.anchor"
                class="reference-label" dir="auto">{{ r.label.text }}</text>
        </g>

        <!-- data labels -->
        <text *ngFor="let t of g.dataLabels" [attr.x]="t.x" [attr.y]="t.y" [attr.text-anchor]="t.anchor"
              class="data-label" [class.inside]="!!t.ink" [style.fill]="t.ink || null" aria-hidden="true">{{ t.text }}</text>

        <!-- category labels -->
        <text *ngFor="let c of g.categoryLabels" [attr.x]="c.x" [attr.y]="c.y" [attr.text-anchor]="c.anchor"
              class="tick" dir="auto">{{ c.text }}</text>
      </svg>

      <ng-container *ngIf="g.legendPosition === 'bottom'">
        <ng-container *ngTemplateOutlet="legendTpl"></ng-container>
      </ng-container>

      <ng-template #legendTpl>
        <ul class="legend">
          <li *ngFor="let item of g.legend">
            <span class="swatch" [style.background]="item.color"></span>
            <span dir="auto">{{ item.name }}</span>
          </li>
        </ul>
      </ng-template>

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
    /* In a dashboard tile the chart fills whatever height the tile has, rather than its own. */
    .chart.compact { margin: 0; block-size: 100%; display: flex; flex-direction: column; }
    .chart.compact svg { flex: 1 1 auto; min-block-size: 0; max-block-size: none; block-size: 100%; }
    .chart.compact .legend { margin-block: 4px; }
    .clickable { cursor: pointer; }
    .clickable:hover, .clickable:focus-visible { opacity: 0.8; }
    .clickable:focus-visible { outline: 2px solid var(--accent-primary); outline-offset: 1px; }
    figcaption { font-weight: 600; margin-block-end: 8px; color: var(--text-primary); }
    svg { inline-size: 100%; block-size: auto; max-block-size: 340px; }
    .grid { stroke: var(--divider-color); stroke-width: 1; }
    .axis { stroke: var(--border-color); stroke-width: 1; }
    .tick { font-size: 11px; fill: var(--text-secondary); }
    /* A thin surface-coloured edge separates touching fills: stacked segments, pie slices. */
    .mark { stroke: var(--bg-card); stroke-width: 1; }
    .dot { stroke: var(--bg-card); stroke-width: 1.5; }
    .area { opacity: 0.22; }
    .centre { font-size: 22px; font-weight: 600; fill: var(--text-primary); }
    .reference { stroke: var(--text-secondary); stroke-width: 1.5; stroke-dasharray: 5 4; }
    .reference-label { font-size: 11px; fill: var(--text-secondary); }
    .data-label { font-size: 10px; fill: var(--text-primary); }
    .data-label.inside { font-weight: 600; }
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

  /** Presentation options; a report chart has none and draws with the defaults. */
  @Input() options: ChartDrawOptions | null = null;

  /** Makes each bar, slice and point a focusable button emitting {@link categoryClick}. */
  @Input() interactive = false;

  /** Fills the host's height instead of taking the chart's natural size — for dashboard tiles. */
  @Input() compact = false;

  /** The category of the bar, slice or point the user activated. Only emitted when interactive. */
  @Output() categoryClick = new EventEmitter<string>();

  g: ChartGeometry | null = null;

  ngOnChanges(): void {
    this.g = this.chart ? buildChartGeometry(this.chart, this.options ?? {}) : null;
  }

  select(category: string, event?: Event): void {
    if (!this.interactive) return;
    // Space would otherwise scroll the page as well as activating the mark.
    event?.preventDefault();
    this.categoryClick.emit(category);
  }
}
