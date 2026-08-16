import { Component, inject, ChangeDetectorRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  BrandingService,
  FAVICON_ACCEPT,
  FAVICON_MAX_BYTES,
  FAVICON_RECOMMENDED_SIZE,
  LOGO_ACCEPT,
  LOGO_DISPLAY_HEIGHT,
  LOGO_MAX_BYTES,
  LOGO_RECOMMENDED_HEIGHT,
  LOGO_RECOMMENDED_MAX_WIDTH,
  SITE_NAME_MAX_LENGTH
} from '@core/services/branding.service';
import { ToastService } from '@core/services/toast.service';

/**
 * Admin-only dialog for what the top banner shows: an uploaded logo, or a site name written
 * per language.
 *
 * <p>Both live here rather than in separate dialogs because they answer one question — what is
 * this installation called — and the answer is a fallback chain: the logo wins, the name shows
 * when there is no logo, and the application name shows when there is neither. Split across two
 * places an admin would have to guess which one is currently winning.</p>
 *
 * <p>In a dialog rather than inline in the account menu because a menu closes on the first
 * click, which would dismiss it the moment the file picker opened.</p>
 */
@Component({
  standalone: true,
  selector: 'app-branding-dialog',
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatDividerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoModule
  ],
  template: `
    <h2 mat-dialog-title class="title">
      <mat-icon aria-hidden="true">branding_watermark</mat-icon>
      {{ 'branding.title' | transloco }}
    </h2>

    <mat-dialog-content>
      <h3 class="section">{{ 'branding.logoSection' | transloco }}</h3>
      <p class="hint">
        {{ 'branding.logoHint' | transloco: {
             recommendedHeight: recommendedHeight,
             recommendedMaxWidth: recommendedMaxWidth,
             displayHeight: displayHeight,
             maxMb: maxMb
           } }}
      </p>

      <div class="preview" [class.empty]="!previewUrl">
        <img *ngIf="previewUrl" [src]="previewUrl"
             [alt]="'branding.logoPreview' | transloco" class="preview-img">
        <span *ngIf="!previewUrl" class="preview-placeholder">
          {{ 'branding.noLogo' | transloco }}
        </span>
      </div>

      <p *ngIf="selectedDimensions" class="dimensions">
        <mat-icon inline [class.warn]="dimensionsWarning">
          {{ dimensionsWarning ? 'warning' : 'check_circle' }}
        </mat-icon>
        <span dir="ltr" class="force-ltr">{{ selectedDimensions }}</span>
        <ng-container *ngIf="dimensionsWarning"> — {{ dimensionsWarning }}</ng-container>
      </p>

      <div class="logo-actions">
        <button mat-stroked-button (click)="fileInput.click()" [disabled]="busy">
          <mat-icon>upload</mat-icon>
          {{ (hasLogo ? 'branding.replaceLogo' : 'branding.uploadLogo') | transloco }}
        </button>
        <button mat-button color="warn" *ngIf="hasLogo" (click)="removeLogo()" [disabled]="busy">
          <mat-icon>delete</mat-icon> {{ 'branding.removeLogo' | transloco }}
        </button>
      </div>

      <input #fileInput type="file" [accept]="accept" hidden (change)="onFileSelected($event)">

      <mat-divider class="divider"></mat-divider>

      <h3 class="section">{{ 'branding.faviconSection' | transloco }}</h3>
      <p class="hint">
        {{ 'branding.faviconHint' | transloco: {
             recommendedSize: faviconRecommendedSize,
             maxKb: faviconMaxKb
           } }}
      </p>

      <div class="favicon-row">
        <!-- Shown at its real tab size beside a magnified copy: an icon that is legible at
             64 px can still be a smudge at 16 px, which is the size that actually ships. -->
        <div class="favicon-preview" [class.empty]="!(faviconUrl$ | async)">
          <ng-container *ngIf="faviconUrl$ | async as favUrl; else noFavicon">
            <img [src]="favUrl" [alt]="'branding.faviconPreview' | transloco" class="favicon-lg">
            <img [src]="favUrl" [alt]="'branding.faviconActualSize' | transloco" class="favicon-sm">
            <span class="favicon-caption">{{ 'branding.faviconActualSize' | transloco }}</span>
          </ng-container>
          <ng-template #noFavicon>
            <span class="preview-placeholder">{{ 'branding.noFavicon' | transloco }}</span>
          </ng-template>
        </div>

        <div class="logo-actions">
          <button mat-stroked-button (click)="faviconInput.click()" [disabled]="busy">
            <mat-icon>upload</mat-icon>
            {{ ((faviconUrl$ | async) ? 'branding.replaceLogo' : 'branding.uploadLogo') | transloco }}
          </button>
          <button mat-button color="warn" *ngIf="faviconUrl$ | async" (click)="removeFavicon()"
                  [disabled]="busy">
            <mat-icon>delete</mat-icon> {{ 'branding.removeLogo' | transloco }}
          </button>
        </div>
      </div>

      <input #faviconInput type="file" [accept]="faviconAccept" hidden
             (change)="onFaviconSelected($event)">

      <mat-divider class="divider"></mat-divider>

      <h3 class="section">{{ 'branding.nameSection' | transloco }}</h3>
      <p class="hint">{{ 'branding.nameHint' | transloco }}</p>

      <!-- One field per language rather than one field mirrored: an installation is called
           something different in each, and a single value would force one audience to read
           the other's name. Each input is forced to its own direction so an Arabic name typed
           into the English field still reads correctly while being typed. -->
      <mat-form-field appearance="outline" class="name-field">
        <mat-label>{{ 'branding.nameEn' | transloco }}</mat-label>
        <input matInput dir="ltr" [(ngModel)]="siteNameEn" [maxlength]="maxNameLength"
               [placeholder]="'branding.namePlaceholderEn' | transloco" [disabled]="busy">
        <!-- dir="ltr": in an RTL paragraph the bidi algorithm renders "0 / 60" as "60 / 0",
             which reads as sixty characters used out of nothing. -->
        <mat-hint align="end"><span dir="ltr">{{ siteNameEn.length }} / {{ maxNameLength }}</span></mat-hint>
      </mat-form-field>

      <mat-form-field appearance="outline" class="name-field">
        <mat-label>{{ 'branding.nameAr' | transloco }}</mat-label>
        <input matInput dir="rtl" [(ngModel)]="siteNameAr" [maxlength]="maxNameLength"
               [placeholder]="'branding.namePlaceholderAr' | transloco" [disabled]="busy">
        <mat-hint align="end"><span dir="ltr">{{ siteNameAr.length }} / {{ maxNameLength }}</span></mat-hint>
      </mat-form-field>

      <p class="hint muted">{{ 'branding.nameClearHint' | transloco }}</p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <mat-spinner *ngIf="busy" diameter="24"></mat-spinner>
      <button mat-button (click)="dialogRef.close()" [disabled]="busy">
        {{ 'common.close' | transloco }}
      </button>
      <button mat-raised-button color="primary" (click)="saveNames()" [disabled]="busy">
        <mat-icon>save</mat-icon> {{ 'common.save' | transloco }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .title { display: flex; align-items: center; gap: 8px; }
    .section { margin: 0 0 8px; font-size: 14px; font-weight: 600; }
    .hint { margin: 0 0 16px; color: var(--text-secondary); font-size: 13px; line-height: 1.5; }
    .hint.muted { margin-top: 4px; }
    .preview {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 88px;
      padding: 16px;
      border: 1px dashed var(--border-color);
      border-radius: 8px;
      background: var(--bg-surface);
    }
    .preview.empty { color: var(--text-secondary); }
    .preview-placeholder { font-size: 13px; }
    .preview-img { max-height: 56px; max-width: 100%; object-fit: contain; }
    .dimensions { margin: 12px 0 0; font-size: 13px; color: var(--text-secondary); }
    .dimensions .warn { color: var(--status-warning); }
    .logo-actions { display: flex; gap: 8px; margin-top: 12px; flex-wrap: wrap; }
    .divider { margin: 24px 0; }
    .name-field { width: 100%; }
    .favicon-row { display: flex; align-items: center; gap: 20px; flex-wrap: wrap; }
    .favicon-preview {
      display: flex;
      align-items: center;
      gap: 12px;
      min-height: 64px;
      padding: 12px 16px;
      border: 1px dashed var(--border-color);
      border-radius: 8px;
      background: var(--bg-surface);
    }
    .favicon-preview.empty { color: var(--text-secondary); }
    .favicon-lg { width: 48px; height: 48px; object-fit: contain; }
    .favicon-sm { width: 16px; height: 16px; object-fit: contain; }
    .favicon-caption { font-size: 11px; color: var(--text-secondary); }
  `]
})
export class BrandingDialogComponent {
  readonly dialogRef = inject<MatDialogRef<BrandingDialogComponent>>(MatDialogRef);
  private branding = inject(BrandingService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private cdr = inject(ChangeDetectorRef);

  readonly accept = LOGO_ACCEPT;
  readonly displayHeight = LOGO_DISPLAY_HEIGHT;
  readonly recommendedHeight = LOGO_RECOMMENDED_HEIGHT;
  readonly recommendedMaxWidth = LOGO_RECOMMENDED_MAX_WIDTH;
  readonly maxMb = LOGO_MAX_BYTES / (1024 * 1024);
  readonly maxNameLength = SITE_NAME_MAX_LENGTH;
  readonly faviconAccept = FAVICON_ACCEPT;
  readonly faviconRecommendedSize = FAVICON_RECOMMENDED_SIZE;
  readonly faviconMaxKb = FAVICON_MAX_BYTES / 1024;
  readonly faviconUrl$ = this.branding.faviconUrl$;

  previewUrl: string | null = null;
  hasLogo = false;
  busy = false;
  selectedDimensions = '';
  dimensionsWarning = '';
  siteNameEn = '';
  siteNameAr = '';

  constructor() {
    // The service outlives the dialog, so without teardown each reopen would leave another
    // subscriber attached to the BehaviorSubject.
    this.branding.logoUrl$.pipe(takeUntilDestroyed()).subscribe(url => {
      this.previewUrl = url;
      this.hasLogo = !!url;
      this.cdr.markForCheck();
    });

    // Seeded from the last loaded state rather than refetched, so reopening the dialog shows
    // what is stored. An upload refreshes the service, which re-emits here.
    this.branding.info$.pipe(takeUntilDestroyed()).subscribe(info => {
      this.siteNameEn = info?.siteNameEn ?? '';
      this.siteNameAr = info?.siteNameAr ?? '';
      this.cdr.markForCheck();
    });
  }

  saveNames(): void {
    this.busy = true;
    this.cdr.markForCheck();
    this.branding.setSiteName(this.siteNameEn.trim(), this.siteNameAr.trim()).subscribe({
      next: () => {
        this.busy = false;
        this.toast.success('branding.nameSaved');
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = false;
        this.toast.error(err, 'branding.nameSaveFailed');
        this.cdr.markForCheck();
      }
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Reset immediately so picking the same file twice still fires a change event.
    input.value = '';
    if (!file) return;

    this.selectedDimensions = '';
    this.dimensionsWarning = '';

    if (file.size > LOGO_MAX_BYTES) {
      this.toast.error(null, 'branding.logoTooLarge', {
        size: this.formatMb(file.size),
        max: this.maxMb
      });
      return;
    }

    // Measure before uploading so the admin is told *why* a logo will look wrong, rather than
    // discovering it once it is already live in the banner.
    this.measure(file).then(size => {
      if (size) {
        this.selectedDimensions = `${size.width} × ${size.height} px`;
        this.dimensionsWarning = this.warnAbout(size);
      }
      this.upload(file);
    });
  }

  private upload(file: File): void {
    this.busy = true;
    this.cdr.markForCheck();
    this.branding.upload(file).subscribe({
      next: () => {
        this.busy = false;
        this.toast.success('branding.logoUpdated');
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = false;
        this.toast.error(err, 'branding.logoUploadFailed');
        this.cdr.markForCheck();
      }
    });
  }

  onFaviconSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    if (file.size > FAVICON_MAX_BYTES) {
      this.toast.error(null, 'branding.faviconTooLarge', {
        size: Math.round(file.size / 1024),
        max: this.faviconMaxKb
      });
      return;
    }

    this.busy = true;
    this.cdr.markForCheck();
    this.branding.uploadFavicon(file).subscribe({
      next: () => {
        this.busy = false;
        this.toast.success('branding.faviconUpdated');
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = false;
        this.toast.error(err, 'branding.faviconUploadFailed');
        this.cdr.markForCheck();
      }
    });
  }

  removeFavicon(): void {
    this.busy = true;
    this.cdr.markForCheck();
    this.branding.removeFavicon().subscribe({
      next: () => {
        this.busy = false;
        this.toast.success('branding.faviconRemoved');
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = false;
        this.toast.error(err, 'branding.faviconRemoveFailed');
        this.cdr.markForCheck();
      }
    });
  }

  removeLogo(): void {
    this.busy = true;
    this.cdr.markForCheck();
    this.branding.remove().subscribe({
      next: () => {
        this.busy = false;
        this.selectedDimensions = '';
        this.dimensionsWarning = '';
        this.toast.success('branding.logoRemoved');
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = false;
        this.toast.error(err, 'branding.logoRemoveFailed');
        this.cdr.markForCheck();
      }
    });
  }

  /** Resolves null when the browser cannot decode the file — the server still validates it. */
  private measure(file: File): Promise<{ width: number; height: number } | null> {
    return new Promise(resolve => {
      const url = URL.createObjectURL(file);
      const img = new Image();
      img.onload = () => {
        URL.revokeObjectURL(url);
        resolve({ width: img.naturalWidth, height: img.naturalHeight });
      };
      img.onerror = () => {
        URL.revokeObjectURL(url);
        resolve(null);
      };
      img.src = url;
    });
  }

  private warnAbout(size: { width: number; height: number }): string {
    if (size.height < LOGO_DISPLAY_HEIGHT) {
      return this.translate('branding.warnShort', { height: LOGO_DISPLAY_HEIGHT });
    }
    if (size.height < LOGO_RECOMMENDED_HEIGHT) {
      return this.translate('branding.warnSoft');
    }
    // Scaled to the banner height, an over-wide logo eats the space the nav buttons need.
    const scaledWidth = Math.round(size.width * (LOGO_DISPLAY_HEIGHT / size.height));
    if (scaledWidth > 200) {
      return this.translate('branding.warnWide');
    }
    return '';
  }

  /** These land in a plain string, not a template, so they cannot use the transloco pipe. */
  private translate(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(key, params);
  }

  private formatMb(bytes: number): string {
    return (bytes / (1024 * 1024)).toFixed(1);
  }
}
