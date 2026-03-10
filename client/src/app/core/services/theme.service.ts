import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly STORAGE_KEY = 'theme-preference';
  private darkMode$ = new BehaviorSubject<boolean>(this.loadPreference());

  isDarkMode$ = this.darkMode$.asObservable();

  constructor() {
    this.applyTheme(this.darkMode$.value);
  }

  toggle(): void {
    const next = !this.darkMode$.value;
    this.darkMode$.next(next);
    this.applyTheme(next);
    localStorage.setItem(this.STORAGE_KEY, JSON.stringify(next));
  }

  isDark(): boolean {
    return this.darkMode$.value;
  }

  private loadPreference(): boolean {
    const stored = localStorage.getItem(this.STORAGE_KEY);
    if (stored !== null) {
      return JSON.parse(stored);
    }
    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
  }

  private applyTheme(dark: boolean): void {
    document.body.classList.toggle('dark-theme', dark);
    document.documentElement.classList.toggle('dark-theme', dark);
  }
}
