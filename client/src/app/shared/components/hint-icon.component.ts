import { Component, Input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

/**
 * An explanatory hint, collapsed to an icon that reveals its text on hover.
 *
 * <p>Admin forms accumulate long "what does this toggle actually do" paragraphs. Left inline they
 * push the fields apart until the form reads as prose with inputs buried in it; the explanation is
 * needed once, when someone first meets the setting, and is noise on every visit after that.</p>
 *
 * <p><b>Not decorative.</b> The icon is focusable and carries the text as its accessible name, so
 * the hint reaches keyboard and screen-reader users too — Material shows the tooltip on focus as
 * well as hover. A hint that only exists on :hover is invisible to anyone not using a mouse.</p>
 */
@Component({
  standalone: true,
  selector: 'app-hint-icon',
  imports: [MatIconModule, MatTooltipModule],
  template: `
    <mat-icon class="hint-icon"
              tabindex="0"
              [matTooltip]="text"
              matTooltipClass="hint-tooltip"
              [matTooltipPosition]="position"
              [attr.aria-label]="text">info_outline</mat-icon>
  `,
  styles: [`
    .hint-icon {
      font-size: 18px;
      width: 18px;
      height: 18px;
      /* Sits on the text baseline of whatever label it follows rather than the line box,
         so it does not nudge a toggle or heading out of alignment. */
      vertical-align: text-bottom;
      color: var(--text-secondary);
      cursor: help;
      outline: none;
    }
    .hint-icon:hover,
    .hint-icon:focus-visible { color: var(--text-primary); }
    /* Focus has to be visible, or a keyboard user cannot tell what they are about to read. */
    .hint-icon:focus-visible {
      border-radius: 50%;
      box-shadow: 0 0 0 2px var(--primary-color, #3f51b5);
    }
  `]
})
export class HintIconComponent {
  /** The already-translated hint text. Callers pass `'key' | transloco`. */
  @Input({ required: true }) text = '';

  /** Overridable for hints near the window edge, where "above" would be clipped. */
  @Input() position: 'above' | 'below' | 'left' | 'right' = 'above';
}
