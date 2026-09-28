/**
 * Relative dates a date filter can hold instead of a fixed one. Mirrors DatePresets.cs — the
 * server resolves the token when a tile runs; this copy only exists to show the viewer which date
 * a token means today, and to offer the presets in the filter bar.
 *
 * <p>The token, not the date, is what the form and the URL keep, so a bookmark or a wall screen
 * keeps meaning "this month" as the months go by.</p>
 */
export interface DatePreset {
  token: string;
  labelKey: string;
}

/** The presets offered in the filter bar, in menu order. */
export const DATE_PRESETS: readonly DatePreset[] = [
  { token: 'today', labelKey: 'user.dashboards.presets.today' },
  { token: 'today-7', labelKey: 'user.dashboards.presets.days7' },
  { token: 'today-30', labelKey: 'user.dashboards.presets.days30' },
  { token: 'week_start', labelKey: 'user.dashboards.presets.weekStart' },
  { token: 'month_start', labelKey: 'user.dashboards.presets.monthStart' },
  { token: 'month_end', labelKey: 'user.dashboards.presets.monthEnd' },
  { token: 'last_month_start', labelKey: 'user.dashboards.presets.lastMonthStart' },
  { token: 'last_month_end', labelKey: 'user.dashboards.presets.lastMonthEnd' },
  { token: 'year_start', labelKey: 'user.dashboards.presets.yearStart' }
];

const OFFSET = /^today([+-])(\d{1,5})$/;

/** True when the value is a preset token rather than a date. */
export function isDatePreset(value: unknown): boolean {
  return typeof value === 'string' && resolveDatePreset(value) !== null;
}

/** The date a token means today, or null when the value is not a token. */
export function resolveDatePreset(value: string, today: Date = new Date()): Date | null {
  const token = value.trim().toLowerCase();
  const t = new Date(today.getFullYear(), today.getMonth(), today.getDate());

  const offset = OFFSET.exec(token);
  if (offset) {
    const days = Number(offset[2]);
    return new Date(t.getFullYear(), t.getMonth(), t.getDate() + (offset[1] === '-' ? -days : days));
  }

  const y = t.getFullYear();
  const m = t.getMonth();
  switch (token) {
    case 'today': return t;
    // Weeks start on Sunday, matching the server.
    case 'week_start': return new Date(y, m, t.getDate() - t.getDay());
    case 'month_start': return new Date(y, m, 1);
    case 'month_end': return new Date(y, m + 1, 0);
    case 'last_month_start': return new Date(y, m - 1, 1);
    case 'last_month_end': return new Date(y, m, 0);
    case 'year_start': return new Date(y, 0, 1);
    default: return null;
  }
}

/** The i18n key naming a token, or null for an offset the menu does not list. */
export function datePresetLabelKey(token: string): string | null {
  return DATE_PRESETS.find(p => p.token === token.trim().toLowerCase())?.labelKey ?? null;
}
