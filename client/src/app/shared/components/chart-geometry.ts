import { ReportChartType, ReportRunChart } from '@core/models/report.model';

/**
 * Pure geometry for {@link ReportChartComponent}: from a chart's categories and series to the
 * rectangles, paths and labels it draws. Kept out of the component so the component is only a
 * template, and so every chart type shares one scale, one set of paddings and one palette.
 *
 * <p>No chart library: the offline bundle admits none, and the Word export draws the same data as
 * native DrawingML, so this only has to agree with that, not with a third renderer.</p>
 */

/** The options the geometry reads — a subset of a dashboard tile's display options. */
export interface ChartDrawOptions {
  legend?: 'auto' | 'hidden' | 'top' | 'bottom';
  dataLabels?: boolean;
  stackPercent?: boolean;
  lineSeries?: string[];
  secondaryAxis?: boolean;
  axisMin?: number | null;
  axisMax?: number | null;
  referenceLine?: number | null;
  referenceLabel?: string | null;
  seriesColors?: Record<string, number>;
}

export interface ChartBar { x: number; y: number; w: number; h: number; fill: string; label: string; value: string; series: string; }
export interface ChartDot { cx: number; cy: number; label: string; value: string; }
export interface ChartLine { segments: string[]; stroke: string; name: string; dots: ChartDot[]; }
export interface ChartArea { path: string; fill: string; }
export interface ChartSlice { path: string; fill: string; label: string; value: string; percent: string; }
export interface ChartTick { pos: number; label: string; }
export interface ChartText {
  x: number; y: number; text: string; anchor: 'start' | 'middle' | 'end';
  /** Set for a label drawn on a mark: the ink that reads on that mark's fill, whatever the theme. */
  ink?: string;
}
export interface ChartRule { x1: number; y1: number; x2: number; y2: number; label: ChartText | null; }

export interface ChartGeometry {
  width: number;
  height: number;
  plot: { left: number; right: number; top: number; bottom: number };
  /** Bars lie on their side: categories down the left, values along the bottom. */
  horizontal: boolean;
  pieLike: boolean;
  bars: ChartBar[];
  lines: ChartLine[];
  areas: ChartArea[];
  slices: ChartSlice[];
  valueTicks: ChartTick[];
  /** A combo's own scale for its line, on the far side; empty without one. */
  secondaryTicks: ChartTick[];
  categoryLabels: ChartText[];
  dataLabels: ChartText[];
  reference: ChartRule | null;
  /** A doughnut's total, in its hole. */
  centre: ChartText | null;
  legend: { name: string; color: string }[];
  legendPosition: 'top' | 'bottom' | null;
}

/**
 * Categorical palette. Fixed rather than themed: a series must keep its colour when the viewer
 * switches between light and dark, or the chart appears to change meaning. The Word writer uses
 * the same ten, in the same order.
 */
export const CHART_PALETTE = [
  '#4f46e5', '#0891b2', '#16a34a', '#ca8a04', '#dc2626',
  '#7c3aed', '#0d9488', '#65a30d', '#ea580c', '#be123c'
];

/**
 * Ink for a label drawn on a fill. The fills are fixed across themes, so the ink is chosen from
 * the fill — dark or white, whichever contrasts more — not from the page's text token, which in
 * the dark theme would put navy text on a red bar.
 */
const DARK_INK = '#0f172a';
const LIGHT_INK = '#ffffff';

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map(v => v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function inkOn(fill: string): string {
  const l = luminance(fill);
  return (l + 0.05) / (luminance(DARK_INK) + 0.05) >= 1.05 / (l + 0.05) ? DARK_INK : LIGHT_INK;
}

const WIDTH = 640;
const HEIGHT = 300;
/** Below this a segment or bar is too small to carry a readable label. */
const MIN_LABEL_SPACE = 14;

export function isPieLike(type: ReportChartType): boolean {
  return type === ReportChartType.Pie || type === ReportChartType.Doughnut;
}

export function isStacked(type: ReportChartType): boolean {
  return type === ReportChartType.StackedColumn || type === ReportChartType.StackedBar;
}

export function isHorizontal(type: ReportChartType): boolean {
  return type === ReportChartType.Bar || type === ReportChartType.StackedBar;
}

export function formatNumber(value: number): string {
  return value.toLocaleString(undefined, { maximumFractionDigits: 2 });
}

/** Axis labels stay short so they do not eat the plot. */
export function abbreviate(value: number): string {
  const abs = Math.abs(value);
  if (abs >= 1_000_000_000) return (value / 1_000_000_000).toFixed(1) + 'B';
  if (abs >= 1_000_000) return (value / 1_000_000).toFixed(1) + 'M';
  if (abs >= 1_000) return (value / 1_000).toFixed(abs >= 10_000 ? 0 : 1) + 'K';
  return value.toFixed(abs < 10 && value % 1 !== 0 ? 1 : 0);
}

function truncate(text: string, max: number): string {
  return text.length > max ? text.slice(0, max - 1) + '…' : text;
}

/** A linear scale from a value domain onto a pixel range. */
class Scale {
  constructor(readonly min: number, readonly max: number, private from: number, private to: number) {}
  at(value: number): number {
    const t = (value - this.min) / (this.max - this.min || 1);
    return this.from + t * (this.to - this.from);
  }
  /** Where zero sits, clamped into the plot, so bars grow from it or from the nearest edge. */
  get base(): number {
    return this.at(Math.min(Math.max(0, this.min), this.max));
  }
}

/** The domain a set of values needs: always including zero, widened by the options. */
function domain(values: number[], options: ChartDrawOptions, extra: (number | null | undefined)[] = []): [number, number] {
  const all = [...values, ...extra.filter((v): v is number => v !== null && v !== undefined)];
  let min = Math.min(0, ...all);
  let max = Math.max(0, ...all);
  if (max === min) max = min + 1;
  if (options.axisMin !== null && options.axisMin !== undefined) min = options.axisMin;
  if (options.axisMax !== null && options.axisMax !== undefined) max = options.axisMax;
  if (max <= min) max = min + 1;
  return [min, max];
}

function ticks(scale: Scale, percent: boolean): ChartTick[] {
  const result: ChartTick[] = [];
  for (let i = 0; i <= 4; i++) {
    const value = scale.min + ((scale.max - scale.min) / 4) * i;
    result.push({ pos: scale.at(value), label: abbreviate(value) + (percent ? '%' : '') });
  }
  return result;
}

export function buildChartGeometry(chart: ReportRunChart, options: ChartDrawOptions = {}): ChartGeometry {
  const type = chart.type;
  const pieLike = isPieLike(type);
  const horizontal = isHorizontal(type);
  const stacked = isStacked(type);
  const percent = stacked && !!options.stackPercent;
  const labels = !!options.dataLabels;

  const colourOf = (series: string, index: number) =>
    CHART_PALETTE[(options.seriesColors?.[series] ?? index) % CHART_PALETTE.length];

  // A combo's line series: those named, else the last. Everything else is a column.
  const lineNames = type === ReportChartType.Combo
    ? new Set((options.lineSeries ?? []).filter(n => chart.series.some(s => s.name === n)).length > 0
        ? options.lineSeries!.filter(n => chart.series.some(s => s.name === n))
        : chart.series.length > 1 ? [chart.series[chart.series.length - 1].name] : [])
    : new Set<string>();
  const secondary = type === ReportChartType.Combo && !!options.secondaryAxis && lineNames.size > 0;

  const legendWanted = options.legend === 'hidden' ? false
    : options.legend === 'top' || options.legend === 'bottom' ? true
    : chart.series.length > 1 || pieLike;

  const g: ChartGeometry = {
    width: WIDTH, height: HEIGHT,
    plot: {
      left: horizontal ? 100 : 56,
      right: WIDTH - (secondary ? 56 : 16),
      top: labels ? 22 : 12,
      bottom: HEIGHT - (horizontal ? 24 : 34)
    },
    horizontal, pieLike,
    bars: [], lines: [], areas: [], slices: [],
    valueTicks: [], secondaryTicks: [], categoryLabels: [], dataLabels: [],
    reference: null, centre: null,
    legend: legendWanted
      ? pieLike
        ? chart.categories.map((c, i) => ({ name: c, color: colourOf(c, i) }))
        : chart.series.map((s, i) => ({ name: s.name, color: colourOf(s.name, i) }))
      : [],
    legendPosition: legendWanted ? (options.legend === 'top' ? 'top' : 'bottom') : null
  };

  if (chart.categories.length === 0 || chart.series.length === 0) return g;

  if (pieLike) {
    buildPie(g, chart, type === ReportChartType.Doughnut, labels, colourOf);
    return g;
  }

  const n = chart.categories.length;
  const { left, right, top, bottom } = g.plot;
  // Categories run along the bottom, or down the side for bars; each owns one equal band.
  const bandStart = horizontal ? top : left;
  const band = ((horizontal ? bottom - top : right - left)) / n;
  const centreOf = (i: number) => bandStart + band * (i + 0.5);

  // Value scale. A stack's extent is its sum, not its tallest member.
  const primarySeries = chart.series.filter(s => !secondary || !lineNames.has(s.name));
  let values: number[];
  if (percent) {
    values = [100];
  } else if (stacked) {
    values = chart.categories.map((_, i) => primarySeries.reduce((sum, s) => sum + Math.max(0, s.values[i] ?? 0), 0));
    values.push(...chart.categories.map((_, i) => primarySeries.reduce((sum, s) => sum + Math.min(0, s.values[i] ?? 0), 0)));
  } else {
    values = primarySeries.flatMap(s => s.values).filter((v): v is number => v !== null);
  }
  const [min, max] = domain(values, options, [options.referenceLine]);
  const scale = horizontal ? new Scale(min, max, left, right) : new Scale(min, max, bottom, top);
  g.valueTicks = ticks(scale, percent);

  let lineScale = scale;
  if (secondary) {
    const lineValues = chart.series.filter(s => lineNames.has(s.name))
      .flatMap(s => s.values).filter((v): v is number => v !== null);
    const [lmin, lmax] = domain(lineValues, {});
    lineScale = new Scale(lmin, lmax, bottom, top);
    g.secondaryTicks = ticks(lineScale, false);
  }

  // Category labels thin out rather than overlap.
  const every = Math.ceil(n / (horizontal ? 12 : 8));
  chart.categories.forEach((text, i) => {
    if (i % every !== 0) return;
    g.categoryLabels.push(horizontal
      ? { x: left - 6, y: centreOf(i) + 4, text: truncate(text, 16), anchor: 'end' }
      : { x: centreOf(i), y: bottom + 16, text: truncate(text, 12), anchor: 'middle' });
  });

  const barSeries = chart.series
    .map((s, index) => ({ s, index }))
    .filter(({ s }) => type !== ReportChartType.Line && type !== ReportChartType.Area && !lineNames.has(s.name));

  if (stacked) {
    buildStacks(g, chart, barSeries, scale, centreOf, band, percent, labels, colourOf);
  } else if (barSeries.length > 0) {
    buildBars(g, chart, barSeries, scale, centreOf, band, labels, colourOf);
  }

  const lineSeries = chart.series
    .map((s, index) => ({ s, index }))
    .filter(({ s }) => type === ReportChartType.Line || type === ReportChartType.Area || lineNames.has(s.name));
  if (lineSeries.length > 0) {
    buildLines(g, lineSeries, lineNames.size > 0 ? lineScale : scale, centreOf, chart.categories,
      type === ReportChartType.Area, labels, colourOf);
  }

  if (options.referenceLine !== null && options.referenceLine !== undefined) {
    const at = scale.at(options.referenceLine);
    const text = options.referenceLabel?.trim() || abbreviate(options.referenceLine);
    g.reference = horizontal
      ? { x1: at, y1: top, x2: at, y2: bottom, label: { x: at + 4, y: top + 10, text, anchor: 'start' } }
      : { x1: left, y1: at, x2: right, y2: at, label: { x: right - 4, y: at - 4, text, anchor: 'end' } };
  }

  return g;
}

type Indexed = { s: { name: string; values: (number | null)[] }; index: number };

function buildBars(
  g: ChartGeometry, chart: ReportRunChart, series: Indexed[], scale: Scale,
  centreOf: (i: number) => number, band: number, labels: boolean,
  colourOf: (name: string, index: number) => string
): void {
  const group = band * 0.7;
  const slot = group / series.length;
  // A 2px surface gap between neighbouring bars keeps them distinct without a border colour.
  const thickness = Math.max(2, slot - (series.length > 1 ? 2 : 0));
  const base = scale.base;

  series.forEach(({ s, index }, k) => {
    s.values.forEach((value, i) => {
      if (value === null) return;
      const offset = centreOf(i) - group / 2 + slot * k;
      const end = scale.at(value);
      const label = chart.categories[i];
      const text = formatNumber(value);
      const fill = colourOf(s.name, index);

      if (g.horizontal) {
        g.bars.push({ x: Math.min(base, end), y: offset, w: Math.abs(end - base), h: thickness, fill, label, value: text, series: s.name });
        if (labels && thickness >= 10) {
          g.dataLabels.push({ x: Math.max(base, end) + 4, y: offset + thickness / 2 + 4, text: abbreviate(value), anchor: 'start' });
        }
      } else {
        g.bars.push({ x: offset, y: Math.min(base, end), w: thickness, h: Math.abs(end - base), fill, label, value: text, series: s.name });
        if (labels && thickness >= MIN_LABEL_SPACE) {
          g.dataLabels.push({ x: offset + thickness / 2, y: Math.min(base, end) - 4, text: abbreviate(value), anchor: 'middle' });
        }
      }
    });
  });
}

function buildStacks(
  g: ChartGeometry, chart: ReportRunChart, series: Indexed[], scale: Scale,
  centreOf: (i: number) => number, band: number, percent: boolean, labels: boolean,
  colourOf: (name: string, index: number) => string
): void {
  const thickness = Math.max(2, band * 0.6);

  chart.categories.forEach((label, i) => {
    const total = series.reduce((sum, { s }) => sum + Math.abs(s.values[i] ?? 0), 0);
    // Positive values stack up from zero and negative ones down, so neither hides the other.
    let up = 0;
    let down = 0;
    for (const { s, index } of series) {
      const raw = s.values[i];
      if (raw === null || raw === 0) continue;
      const value = percent ? (total === 0 ? 0 : (Math.abs(raw) / total) * 100) * Math.sign(raw) : raw;
      const from = value >= 0 ? up : down;
      const to = from + value;
      if (value >= 0) up = to; else down = to;

      const a = scale.at(from);
      const b = scale.at(to);
      const offset = centreOf(i) - thickness / 2;
      const text = percent ? `${formatNumber(raw)} (${value.toFixed(1)}%)` : formatNumber(raw);
      const bar: ChartBar = g.horizontal
        ? { x: Math.min(a, b), y: offset, w: Math.abs(b - a), h: thickness, fill: colourOf(s.name, index), label, value: text, series: s.name }
        : { x: offset, y: Math.min(a, b), w: thickness, h: Math.abs(b - a), fill: colourOf(s.name, index), label, value: text, series: s.name };
      g.bars.push(bar);

      const room = g.horizontal ? bar.w : bar.h;
      if (labels && room >= MIN_LABEL_SPACE + 6) {
        g.dataLabels.push({
          x: bar.x + bar.w / 2, y: bar.y + bar.h / 2 + 4,
          text: percent ? `${Math.round(Math.abs(value))}%` : abbreviate(raw), anchor: 'middle', ink: inkOn(bar.fill)
        });
      }
    }
  });
}

function buildLines(
  g: ChartGeometry, series: Indexed[], scale: Scale, centreOf: (i: number) => number,
  categories: string[], area: boolean, labels: boolean,
  colourOf: (name: string, index: number) => string
): void {
  const base = scale.base;
  for (const { s, index } of series) {
    const stroke = colourOf(s.name, index);
    const dots: ChartDot[] = [];
    // A null breaks the line into runs rather than dropping it to zero.
    const runs: { x: number; y: number }[][] = [[]];

    s.values.forEach((value, i) => {
      if (value === null) {
        if (runs[runs.length - 1].length > 0) runs.push([]);
        return;
      }
      const point = { x: centreOf(i), y: scale.at(value) };
      runs[runs.length - 1].push(point);
      dots.push({ cx: point.x, cy: point.y, label: categories[i], value: formatNumber(value) });
      if (labels && s.values.length <= 24) {
        g.dataLabels.push({ x: point.x, y: point.y - 8, text: abbreviate(value), anchor: 'middle' });
      }
    });

    const filled = runs.filter(r => r.length > 0);
    g.lines.push({ segments: filled.map(r => r.map(p => `${p.x},${p.y}`).join(' ')), stroke, name: s.name, dots });

    if (area) {
      for (const run of filled) {
        const path = `M ${run[0].x} ${base} ` + run.map(p => `L ${p.x} ${p.y}`).join(' ') +
          ` L ${run[run.length - 1].x} ${base} Z`;
        g.areas.push({ path, fill: stroke });
      }
    }
  }
}

function buildPie(
  g: ChartGeometry, chart: ReportRunChart, doughnut: boolean, labels: boolean,
  colourOf: (name: string, index: number) => string
): void {
  const values = chart.series[0].values.map(v => Math.max(0, v ?? 0));
  const total = values.reduce((sum, v) => sum + v, 0);
  if (total <= 0) return;

  const cx = g.width / 2;
  const cy = g.height / 2;
  const r = Math.min(g.width, g.height) / 2 - 20;
  const inner = doughnut ? r * 0.58 : 0;
  let angle = -Math.PI / 2;

  values.forEach((value, i) => {
    if (value === 0) return;
    // A single 100% slice cannot be drawn as an arc from a point back to itself.
    const sweep = Math.min((value / total) * Math.PI * 2, Math.PI * 2 - 0.0001);
    const end = angle + sweep;
    const large = sweep > Math.PI ? 1 : 0;
    const p = (radius: number, a: number) => `${cx + radius * Math.cos(a)} ${cy + radius * Math.sin(a)}`;

    const path = doughnut
      ? `M ${p(r, angle)} A ${r} ${r} 0 ${large} 1 ${p(r, end)} L ${p(inner, end)} A ${inner} ${inner} 0 ${large} 0 ${p(inner, angle)} Z`
      : `M ${cx} ${cy} L ${p(r, angle)} A ${r} ${r} 0 ${large} 1 ${p(r, end)} Z`;

    const share = value / total;
    const fill = colourOf(chart.categories[i], i);
    g.slices.push({
      path, fill, label: chart.categories[i],
      value: formatNumber(value), percent: (share * 100).toFixed(1) + '%'
    });

    // Only slices big enough to hold a label get one; the legend and tooltip cover the rest.
    if (labels && share >= 0.05) {
      const mid = angle + sweep / 2;
      const at = doughnut ? (r + inner) / 2 : r * 0.65;
      g.dataLabels.push({ x: cx + at * Math.cos(mid), y: cy + at * Math.sin(mid) + 4, text: Math.round(share * 100) + '%', anchor: 'middle', ink: inkOn(fill) });
    }
    angle = end;
  });

  if (doughnut) {
    g.centre = { x: cx, y: cy + 6, text: abbreviate(total), anchor: 'middle' };
  }
}
