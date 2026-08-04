import { Component, inject, ChangeDetectorRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import {
  BrandingService,
  LOGO_ACCEPT,
  LOGO_DISPLAY_HEIGHT,
  LOGO_MAX_BYTES,
  LOGO_RECOMMENDED_HEIGHT,
  LOGO_RECOMMENDED_MAX_WIDTH
} from '@core/services/branding.service';
import { ToastService } from '@core/services/toast.service';

/**
 * Admin-only dialog for setting the banner logo. Lives in a dialog rather than inline in the
 * account menu because a menu closes on the first click, which would dismiss it the moment the
 * file picker opened.
 */
@Component({
  standalone: true,
  selector: 'app-logo-upload-dialog',
  imports: [CommonModule, MatButtonModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <h2 mat-dialog-title class="title">
      <mat-icon aria-hidden="true">image</mat-icon> Website logo
    </h2>

    <mat-dialog-content>
      <p class="hint">
        Shown in the top banner in place of the site name. Recommended:
        <strong>{{ recommendedHeight }} px tall</strong>, up to {{ recommendedMaxWidth }} px wide
        (it renders at {{ displayHeight }} px tall, so double that keeps it sharp on high-density
        screens). PNG or WebP with a transparent background works best. Max {{ maxMb }} MB.
      </p>

      <div class="preview" [class.empty]="!previewUrl">
        <img *ngIf="previewUrl" [src]="previewUrl" alt="Logo preview" class="preview-img">
        <span *ngIf="!previewUrl" class="preview-placeholder">
          No logo set — the banner shows the site name.
        </span>
      </div>

      <p *ngIf="selectedDimensions" class="dimensions">
        <mat-icon inline [class.warn]="dimensionsWarning">
          {{ dimensionsWarning ? 'warning' : 'check_circle' }}
        </mat-icon>
        {{ selectedDimensions }}<ng-container *ngIf="dimensionsWarning"> — {{ dimensionsWarning }}</ng-container>
      </p>

      <input #fileInput type="file" [accept]="accept" hidden (change)="onFileSelected($event)">
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button color="warn" *ngIf="hasLogo && !busy" (click)="remove()">
        <mat-icon>delete</mat-icon> Remove
      </button>
      <span class="spacer"></span>
      <mat-spinner *ngIf="busy" diameter="24"></mat-spinner>
      <button mat-button (click)="dialogRef.close()" [disabled]="busy">Close</button>
      <button mat-raised-button color="primary" (click)="fileInput.click()" [disabled]="busy">
        <mat-icon>upload</mat-icon> {{ hasLogo ? 'Replace' : 'Upload' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .title { display: flex; align-items: center; gap: 8px; }
    .hint { margin: 0 0 16px; color: var(--text-secondary); font-size: 13px; line-height: 1.5; }
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
    .spacer { flex: 1 1 auto; }
  `]
})
export class LogoUploadDialogComponent {
  readonly dialogRef = inject<MatDialogRef<LogoUploadDialogComponent>>(MatDialogRef);
  private branding = inject(BrandingService);
  private toast = inject(ToastService);
  private cdr = inject(ChangeDetectorRef);

  readonly accept = LOGO_ACCEPT;
  readonly displayHeight = LOGO_DISPLAY_HEIGHT;
  readonly recommendedHeight = LOGO_RECOMMENDED_HEIGHT;
  readonly recommendedMaxWidth = LOGO_RECOMMENDED_MAX_WIDTH;
  readonly maxMb = LOGO_MAX_BYTES / (1024 * 1024);

  previewUrl: string | null = null;
  hasLogo = false;
  busy = false;
  selectedDimensions = '';
  dimensionsWarning = '';

  constructor() {
    // The service outlives the dialog, so without teardown each reopen would leave another
    // subscriber attached to the BehaviorSubject.
    this.branding.logoUrl$.pipe(takeUntilDestroyed()).subscribe(url => {
      this.previewUrl = url;
      this.hasLogo = !!url;
      this.cdr.markForCheck();
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
      this.toast.error(null, `That image is ${this.formatMb(file.size)} MB. The limit is ${this.maxMb} MB.`);
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
        this.toast.success('Logo updated');
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = false;
        this.toast.error(err, 'Failed to upload the logo');
        this.cdr.markForCheck();
      }
    });
  }

  remove(): void {
    this.busy = true;
    this.cdr.markForCheck();
    this.branding.remove().subscribe({
      next: () => {
        this.busy = false;
        this.selectedDimensions = '';
        this.dimensionsWarning = '';
        this.toast.success('Logo removed');
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.busy = false;
        this.toast.error(err, 'Failed to remove the logo');
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
      return `shorter than the ${LOGO_DISPLAY_HEIGHT} px banner, so it will look small`;
    }
    if (size.height < LOGO_RECOMMENDED_HEIGHT) {
      return 'may look soft on high-density screens';
    }
    // Scaled to the banner height, an over-wide logo eats the space the nav buttons need.
    const scaledWidth = Math.round(size.width * (LOGO_DISPLAY_HEIGHT / size.height));
    if (scaledWidth > 200) {
      return 'very wide, so it will be capped to fit beside the navigation';
    }
    return '';
  }

  private formatMb(bytes: number): string {
    return (bytes / (1024 * 1024)).toFixed(1);
  }
}
