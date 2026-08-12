import { inject, Injectable, OnDestroy } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';

/**
 * Translates the paginator's own labels.
 *
 * Angular Material ships these strings inside `MatPaginatorIntl` rather than the template, so
 * they are invisible to Transloco and stay English no matter what the rest of the page does.
 * Replacing the provider is the supported way to localize them.
 *
 * <p>The range label is rebuilt rather than string-concatenated because "1 – 10 of 11" reorders
 * in Arabic; interpolating a single catalog entry lets the translation decide the word order.
 * `changes.next()` on every language switch is what makes already-rendered paginators re-read
 * the labels — without it the table you are looking at keeps the previous language.</p>
 */
@Injectable()
export class LocalizedPaginatorIntl extends MatPaginatorIntl implements OnDestroy {
  private transloco = inject(TranslocoService);
  private sub: Subscription;

  constructor() {
    super();
    this.sub = this.transloco.langChanges$.subscribe(() => {
      this.applyLabels();
      this.changes.next();
    });
    this.applyLabels();
  }

  ngOnDestroy(): void {
    this.sub.unsubscribe();
  }

  private applyLabels(): void {
    this.itemsPerPageLabel = this.transloco.translate('paginator.itemsPerPage');
    this.nextPageLabel = this.transloco.translate('paginator.next');
    this.previousPageLabel = this.transloco.translate('paginator.previous');
    this.firstPageLabel = this.transloco.translate('paginator.first');
    this.lastPageLabel = this.transloco.translate('paginator.last');
  }

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0 || pageSize === 0) {
      return this.transloco.translate('paginator.rangeEmpty', { total: length });
    }

    const total = Math.max(length, 0);
    const start = page * pageSize;
    // Guard against a start index past the end, which happens when the page size grows
    // while the user is on a later page.
    const end = start < total ? Math.min(start + pageSize, total) : start + pageSize;

    return this.transloco.translate('paginator.range', { start: start + 1, end, total });
  };
}
