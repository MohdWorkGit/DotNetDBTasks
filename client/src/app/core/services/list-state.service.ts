import { Injectable } from '@angular/core';

/**
 * Remembers a list page's filter/sort/page state across navigation.
 *
 * Every route back to a list page is a fresh component activation, so filters declared as
 * component fields reset to their defaults. Parking the state here instead survives all of
 * them — routerLink, post-save navigate, the nav bar, and the browser Back button — without
 * each caller having to thread state through the navigation.
 *
 * sessionStorage rather than localStorage: the state should survive a refresh but not outlive
 * the tab. A filter set days ago that silently makes a list look empty is worse than retyping
 * it. (Contrast ThemeService, which uses localStorage because a theme *should* outlive the tab.)
 */
@Injectable({ providedIn: 'root' })
export class ListStateService {
  private readonly PREFIX = 'list-state:';

  save<T>(key: string, state: T): void {
    try {
      sessionStorage.setItem(this.PREFIX + key, JSON.stringify(state));
    } catch {
      // Private-browsing modes and full quotas both throw here. Losing the saved position is
      // not worth breaking the page over.
    }
  }

  /**
   * Returns null when nothing is stored, or when what is stored cannot be parsed — the shape
   * changes whenever a filter is added, so an entry written by an older build must degrade to
   * "start fresh" rather than throw.
   */
  load<T>(key: string): T | null {
    try {
      const raw = sessionStorage.getItem(this.PREFIX + key);
      return raw === null ? null : JSON.parse(raw) as T;
    } catch {
      return null;
    }
  }

  clear(key: string): void {
    try {
      sessionStorage.removeItem(this.PREFIX + key);
    } catch {
      // See save().
    }
  }
}
