import { Component, Input, Output, EventEmitter, OnChanges, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReportRunChart } from '@core/models/report.model';
import { abbreviate, formatNumber } from './chart-geometry';

/** Steps in the sequential ramp — the --heat-1 … --heat-7 tokens in styles.scss. */
const STEPS = 7;

interface Cell { value: number | null; text: string; step: number; title: string; }

/**
 * Rows × columns, each cell shaded by its value on a single-hue sequential ramp. The rows are the
 * tile's category column and the columns its split-by column, pivoted by the server.
 *
 * <p>The ramp is seven discrete steps rather than a continuous blend so each step's text colour
 * can be chosen for contrast against it (the --heat-ink-N tokens), in both themes. On a light
 * page the ramp runs light to dark; on a dark page it is reversed, so "near zero" always recedes
 * toward the surface.</p>
 *
 * <p>A real table: the screen reader reads it as one, with row and column headers.</p>
 */
@Component({
  selector: 'app-heatmap-chart',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="heatmap">
      <table>
        <thead>
          <tr>
            <th scope="col" class="corner"></th>
            <th scope="col" *ngFor="let c of columns" dir="auto" [title]="c">{{ c }}</th>
          </tr>
        </thead>
        <tbody>
          <tr *ngFor="let row of rows; let r = index">
            <th scope="row" dir="auto" [title]="row">
              <button type="button" class="row-label" [disabled]="!interactive" (click)="categoryClick.emit(row)">{{ row }}</button>
            </th>
            <td *ngFor="let cell of cells[r]" [ngClass]="cell.value === null ? 'empty' : 'step-' + cell.step"
                [title]="cell.title" dir="ltr">{{ showValues ? cell.text : '' }}</td>
          </tr>
        </tbody>
      </table>
      <div class="scale" aria-hidden="true">
        <span dir="ltr">{{ minText }}</span>
        <span class="ramp"><span *ngFor="let s of stepList" [ngClass]="'step-' + s"></span></span>
        <span dir="ltr">{{ maxText }}</span>
      </div>
    </div>
  `,
  styles: [`
    .heatmap { block-size: 100%; display: flex; flex-direction: column; gap: 6px; }
    table { flex: 1 1 auto; inline-size: 100%; block-size: 100%; border-collapse: separate; border-spacing: 2px; table-layout: fixed; font-size: 11px; }
    th { font-weight: 500; color: var(--text-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding: 0 4px; }
    thead th { text-align: center; }
    tbody th { text-align: start; inline-size: 22%; }
    .corner { inline-size: 22%; }
    td { text-align: center; border-radius: 3px; overflow: hidden; white-space: nowrap; }
    td.empty { background: var(--bg-surface); }
    .row-label {
      all: unset; display: block; max-inline-size: 100%; overflow: hidden; text-overflow: ellipsis;
      white-space: nowrap; color: var(--text-secondary);
    }
    .row-label:not(:disabled) { cursor: pointer; text-decoration: underline dotted; }
    .row-label:focus-visible { outline: 2px solid var(--accent-primary); outline-offset: 1px; }
    .scale { display: flex; align-items: center; gap: 6px; font-size: 11px; color: var(--text-secondary); }
    .ramp { display: flex; flex: 0 1 160px; block-size: 8px; }
    .ramp span { flex: 1 1 0; }
    .step-1 { background: var(--heat-1); color: var(--heat-ink-1); }
    .step-2 { background: var(--heat-2); color: var(--heat-ink-2); }
    .step-3 { background: var(--heat-3); color: var(--heat-ink-3); }
    .step-4 { background: var(--heat-4); color: var(--heat-ink-4); }
    .step-5 { background: var(--heat-5); color: var(--heat-ink-5); }
    .step-6 { background: var(--heat-6); color: var(--heat-ink-6); }
    .step-7 { background: var(--heat-7); color: var(--heat-ink-7); }
  `]
})
export class HeatmapChartComponent implements OnChanges {
  @Input({ required: true }) chart!: ReportRunChart;
  @Input() interactive = false;
  /** Emits the row's category — the tile's own category column, which drill-through acts on. */
  @Output() categoryClick = new EventEmitter<string>();

  readonly stepList = Array.from({ length: STEPS }, (_, i) => i + 1);
  rows: string[] = [];
  columns: string[] = [];
  cells: Cell[][] = [];
  minText = '';
  maxText = '';
  /** Past a dozen columns the cells are too narrow for numbers; the tooltip keeps them. */
  showValues = true;

  ngOnChanges(): void {
    this.rows = this.chart.categories;
    this.columns = this.chart.series.map(s => s.name);
    this.showValues = this.columns.length <= 12;

    const all = this.chart.series.flatMap(s => s.values).filter((v): v is number => v !== null);
    const min = all.length ? Math.min(...all) : 0;
    const max = all.length ? Math.max(...all) : 0;
    const span = max - min;
    this.minText = abbreviate(min);
    this.maxText = abbreviate(max);

    this.cells = this.rows.map((row, r) => this.chart.series.map(s => {
      const value = s.values[r] ?? null;
      const step = value === null ? 0
        : span === 0 ? Math.ceil(STEPS / 2)
        : 1 + Math.min(STEPS - 1, Math.floor(((value - min) / span) * STEPS));
      return {
        value, step,
        text: value === null ? '' : abbreviate(value),
        title: `${row} · ${s.name}: ${value === null ? '—' : formatNumber(value)}`
      };
    }));
  }
}
