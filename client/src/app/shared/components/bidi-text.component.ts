import { ChangeDetectionStrategy, Component, HostBinding, Input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoService } from '@jsverse/transloco';

/** One stretch of text; `dir` is set on the stretches that must be laid out on their own. */
export interface BidiSegment { text: string; dir?: 'ltr' | 'rtl'; }

export type TextDirection = 'ltr' | 'rtl';

/** Words, not letters: `d.DEPT_CODE` is one word however long, like an Arabic word. */
const ARABIC_WORD = /[ء-يٱ-ۓﭐ-﷿ﹰ-﻿][ء-ْٱ-ۓﭐ-﷿ﹰ-﻿]*/g;
const LATIN_WORD = /[A-Za-z_@][A-Za-z0-9_.@]*/g;

/**
 * How strongly the page's language wins: the model is asked to write in it, so the text is in
 * the other language only when that clearly dominates. Without the head start an English
 * sentence quoting two Arabic column labels ("Shows "اسم الإدارة" and "عدد الموظفين"") flipped
 * to right-to-left, and an Arabic one naming three columns flipped the other way.
 */
const PREFERRED_WEIGHT = 4;

/**
 * An English (or SQL) stretch inside an Arabic sentence: it starts and ends on a letter, digit,
 * @param, bracket, bracket-quote or quote, and runs over anything that is not Arabic and not a
 * line break — so `COUNT(e.EMP_ID)`, `'Active'`, `[Order Date]` or `GROUP BY d.DEPT_CODE` stay
 * one unit. A trailing comma or full stop is left out, so it falls on the Arabic side.
 */
const LATIN_RUN = /[A-Za-z0-9_@[('"`*][^؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿\n]*[A-Za-z0-9_\])'"`*]|[A-Za-z0-9_@]/g;

/** An Arabic stretch inside an English sentence, such as a column alias. */
const ARABIC_RUN = /[؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿][^A-Za-z\n]*[؀-ۿݐ-ݿࢠ-ࣿﭐ-﷿ﹰ-﻿]|[؀-ۿ]/g;

/**
 * The direction a paragraph reads in, by which language carries it — not by its first strong
 * character, which is what dir="auto" uses: an Arabic sentence about SQL often starts with a
 * table name, and that laid the whole sentence out left-to-right.
 */
export function textDirection(text: string, preferred: TextDirection): TextDirection {
  const arabic = (text.match(ARABIC_WORD) ?? []).length;
  const latin = (text.match(LATIN_WORD) ?? []).length;
  if (preferred === 'rtl') return arabic > 0 && arabic * PREFERRED_WEIGHT >= latin ? 'rtl' : 'ltr';
  return arabic > 0 && latin * PREFERRED_WEIGHT < arabic ? 'rtl' : 'ltr';
}

/** Splits the text so every stretch in the other script can be isolated with its own direction. */
export function bidiSegments(text: string, direction: TextDirection): BidiSegment[] {
  const run = direction === 'rtl' ? LATIN_RUN : ARABIC_RUN;
  const isolated: TextDirection = direction === 'rtl' ? 'ltr' : 'rtl';
  const segments: BidiSegment[] = [];
  let at = 0;
  for (const match of text.matchAll(new RegExp(run.source, 'g'))) {
    const start = match.index ?? 0;
    if (start > at) segments.push({ text: text.slice(at, start) });
    segments.push({ text: match[0], dir: isolated });
    at = start + match[0].length;
  }
  if (at < text.length) segments.push({ text: text.slice(at) });
  return segments;
}

/**
 * Text that mixes Arabic with English or SQL — what the AI model writes about a query on an
 * Arabic page. Lays the paragraph out in the language that carries it, and each stretch of the
 * other script in its own isolate, so `COUNT(e.EMP_ID)` or `'Active'` inside an Arabic sentence
 * keeps its order instead of having its brackets and quotes turned around.
 */
@Component({
  standalone: true,
  selector: 'app-bidi-text',
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // One line on purpose: this is inline content, and template whitespace would show as spaces.
  template: `<ng-container *ngFor="let s of segments"><bdi *ngIf="s.dir; else plain" [attr.dir]="s.dir">{{ s.text }}</bdi><ng-template #plain>{{ s.text }}</ng-template></ng-container>`,
  styles: [`:host { unicode-bidi: isolate; } bdi { unicode-bidi: isolate; }`]
})
export class BidiTextComponent {
  segments: BidiSegment[] = [];

  @HostBinding('attr.dir') dir: TextDirection = 'ltr';

  private readonly preferred = pageDirection(inject(TranslocoService));

  @Input() set text(value: string | null | undefined) {
    const text = value ?? '';
    this.dir = textDirection(text, this.preferred);
    this.segments = bidiSegments(text, this.dir);
  }
}

/** The page language's direction, the head start {@link textDirection} gives. */
export function pageDirection(transloco: TranslocoService): TextDirection {
  return transloco.getActiveLang() === 'ar' ? 'rtl' : 'ltr';
}
