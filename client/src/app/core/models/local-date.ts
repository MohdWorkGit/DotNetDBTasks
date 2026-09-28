/**
 * Calendar dates as the viewer sees them. The date picker hands back a Date at local midnight;
 * toISOString() would shift that to UTC first, so east of UTC the picked day became the day
 * before. These build and read "yyyy-mm-dd" from the local parts instead.
 */

/** The local calendar date as yyyy-mm-dd. */
export function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** yyyy-mm-dd (any time part ignored) as a local-midnight Date, or null when it is not one. */
export function fromIsoDate(raw: string | null | undefined): Date | null {
  if (!raw) return null;
  const [y, m, d] = raw.split('T')[0].split('-').map(Number);
  return y && m && d ? new Date(y, m - 1, d) : null;
}
