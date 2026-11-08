import { Component, EventEmitter, Input, OnChanges, Output, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AuthService } from '@core/services/auth.service';
import { ConfirmService } from '@core/services/confirm.service';
import { ToastService } from '@core/services/toast.service';
import { TransferService } from '@core/services/transfer.service';
import { TransferKind } from '@core/models/transfer.model';
import { PERM } from '@core/models/permissions';
import { toIsoDate } from '@core/models/local-date';
import { ImportResultDialogComponent } from './import-result-dialog.component';

/**
 * The "Backup" menu of the report and dashboard lists: export all, import a file, and whether
 * an export carries what the items need on another system (their queries; a dashboard's
 * drill-through reports too). A row's own export button calls {@link exportOne} on this
 * component, so the two exports follow one rule.
 *
 * <p>Including the queries puts their SQL in the file, so the option is offered only to someone
 * who may export queries — and, for a dashboard, reports. The server checks the same.</p>
 */
@Component({
  selector: 'app-transfer-menu',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCheckboxModule, MatIconModule, MatMenuModule, MatTooltipModule, TranslocoModule],
  template: `
    <ng-container *ngIf="canTransfer">
      <input #fileInput type="file" accept=".json,application/json" hidden (change)="onFileSelected($event)">
      <button mat-stroked-button type="button" [matMenuTriggerFor]="menu" [disabled]="busy"
              [matTooltip]="('admin.transfer.backupTip_' + kind) | transloco">
        <mat-icon>{{ busy ? 'hourglass_top' : 'backup' }}</mat-icon>
        {{ 'admin.transfer.backup' | transloco }}
        <mat-icon iconPositionEnd>arrow_drop_down</mat-icon>
      </button>
      <mat-menu #menu="matMenu">
        <button mat-menu-item type="button" (click)="exportOne(null, '')">
          <mat-icon>file_download</mat-icon> {{ ('admin.transfer.exportAll_' + kind) | transloco }}
        </button>
        <button mat-menu-item type="button" (click)="fileInput.click()">
          <mat-icon>file_upload</mat-icon> {{ 'admin.transfer.import' | transloco }}
        </button>
        <!-- A setting, not an action: clicking it must not close the menu. -->
        <div mat-menu-item class="option" (click)="$event.stopPropagation()" [disabled]="!canIncludeDependencies"
             [matTooltip]="(canIncludeDependencies ? 'admin.transfer.includeHint_' + kind : 'admin.transfer.includeNotAllowed') | transloco">
          <mat-checkbox [checked]="includeDependencies" [disabled]="!canIncludeDependencies"
                        (change)="includeDependencies = $event.checked">
            {{ ('admin.transfer.include_' + kind) | transloco }}
          </mat-checkbox>
        </div>
      </mat-menu>
    </ng-container>
  `,
  styles: [`.option { block-size: auto; min-block-size: 48px; }`]
})
export class TransferMenuComponent implements OnChanges {
  @Input({ required: true }) kind!: TransferKind;
  /** Something was imported; the list should reload. */
  @Output() imported = new EventEmitter<void>();

  private transfer = inject(TransferService);
  private auth = inject(AuthService);
  private toast = inject(ToastService);
  private confirm = inject(ConfirmService);
  private dialog = inject(MatDialog);
  private transloco = inject(TranslocoService);
  private cdr = inject(ChangeDetectorRef);

  canTransfer = false;
  canIncludeDependencies = false;
  includeDependencies = false;
  busy = false;

  ngOnChanges(): void {
    this.canTransfer = this.auth.has(this.kind === 'report' ? PERM.reportsTransfer : PERM.dashboardsTransfer);
    this.canIncludeDependencies = this.auth.has(PERM.queriesTransfer)
      && (this.kind === 'report' || this.auth.has(PERM.reportsTransfer));
    this.includeDependencies = this.canIncludeDependencies;
  }

  /** One item by id, or every item of the kind when <c>id</c> is null. */
  exportOne(id: string | null, name: string): void {
    const fallback = this.kind;
    this.transfer.export(this.kind, id, this.includeDependencies && this.canIncludeDependencies).subscribe({
      next: blob => {
        const stamp = toIsoDate(new Date());
        this.transfer.save(blob, id
          ? `${this.transfer.slug(name, fallback)}-${stamp}.json`
          : `${fallback}s-backup-${stamp}.json`);
        if (!id) this.toast.success('admin.transfer.downloaded');
      },
      error: err => this.toast.error(err, 'admin.transfer.exportFailed')
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Cleared first so picking the same file again still fires a change event.
    input.value = '';
    if (!file) return;

    this.confirm.askThen({
      titleKey: 'admin.transfer.importTitle',
      messageKey: 'admin.transfer.importMessage',
      params: { fileName: file.name },
      confirmText: this.transloco.translate('admin.transfer.importConfirm')
    }, () => this.runImport(file));
  }

  private runImport(file: File): void {
    this.busy = true;
    this.cdr.detectChanges();
    this.transfer.import(file).subscribe({
      next: result => {
        this.busy = false;
        this.cdr.detectChanges();
        this.dialog.open(ImportResultDialogComponent, {
          data: result, width: '600px', autoFocus: 'dialog', ariaModal: true
        });
        // Stale the moment anything came in, warnings or not.
        this.imported.emit();
      },
      error: err => {
        this.busy = false;
        this.toast.error(err, 'admin.transfer.importFailed');
        this.cdr.detectChanges();
      }
    });
  }
}
