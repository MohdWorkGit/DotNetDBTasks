/** One logged parameter, ready to display. */
export interface LogParameter {
  name: string;
  /** One entry for a plain value; several for a multi-select, which travels as a JSON array string. */
  values: string[];
  /** True when the parameter was sent with nothing in it — shown as "(empty)" rather than a blank. */
  isEmpty: boolean;
}

/**
 * Turns an execution log's parameter map into display rows. A multi-value parameter is stored
 * as the JSON-array string the client sent (see query-execute's buildParams), so it is unpacked
 * here — otherwise the log would show `["North","South"]` with its brackets and quotes.
 */
export function parseLogParameters(params: Record<string, string> | null | undefined): LogParameter[] {
  if (!params) return [];
  return Object.entries(params).map(([name, raw]) => {
    const values = unpack(raw);
    return { name, values, isEmpty: values.length === 0 || values.every(v => v === '') };
  });
}

function unpack(raw: string | null | undefined): string[] {
  if (raw == null) return [];
  const trimmed = raw.trim();
  if (trimmed.startsWith('[') && trimmed.endsWith(']')) {
    try {
      const parsed = JSON.parse(trimmed);
      if (Array.isArray(parsed)) return parsed.map(v => (v == null ? '' : String(v)));
    } catch {
      // Not JSON after all — a value that merely looks like a list. Show it as typed.
    }
  }
  return [raw];
}
