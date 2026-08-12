import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoModule } from '@jsverse/transloco';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Observable } from 'rxjs';
import { Role } from '@core/models/dynamic-query.model';
import { ToastService } from '@core/services/toast.service';

export type UserEditMode = 'username' | 'password' | 'roles' | 'result';

export interface UserEditDialogData {
  mode: UserEditMode;
  username: string;
  /** All assignable roles — 'roles' mode only. */
  roles?: Role[];
  /** Pre-selected role ids — 'roles' mode only. */
  selectedRoleIds?: string[];
  /** The generated password to display — 'result' mode only. */
  tempPassword?: string;
  /**
   * Performs the save. Endpoint-agnostic, following the old-rows-dialog precedent.
   * The dialog stays open and surfaces the error if this fails, so the user does
   * not lose what they typed.
   */
  save?: (value: string & string[] | string | string[]) => Observable<unknown>;
}

/**
 * Replaces four hand-rolled `<div class="overlay">` modals in user-management that
 * had no dialog role, no focus trap, no focus restore, no Escape handling and no
 * background inerting — the backdrop-click dismiss was unreachable by keyboard.
 * MatDialog provides all of that, so this is the WCAG-conformant equivalent.
 */
@Component({
  standalone: true,
  selector: 'app-user-edit-dialog',
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule, TranslocoModule],
  template: `
    <h2 mat-dialog-title>{{ title }}</h2>
    <mat-dialog-content>
      <p class="subject" *ngIf="data.mode !== 'result'">User: {{ data.username }}</p>

      <mat-form-field *ngIf="data.mode === 'username'" appearance="outline" class="full-width">
        <mat-label>{{ 'admin.users.newUsername' | transloco }}</mat-label>
        <input matInput [(ngModel)]="newUsername" name="newUsername" cdkFocusInitial>
      </mat-form-field>

      <mat-form-field *ngIf="data.mode === 'password'" appearance="outline" class="full-width">
        <mat-label>{{ 'admin.users.newPassword' | transloco }}</mat-label>
        <input matInput [(ngModel)]="newPassword" name="newPassword" type="password" cdkFocusInitial>
        <mat-hint>{{ 'admin.users.passwordMinHint' | transloco }}</mat-hint>
      </mat-form-field>

      <mat-form-field *ngIf="data.mode === 'roles'" appearance="outline" class="full-width">
        <mat-label>{{ 'admin.users.roles' | transloco }}</mat-label>
        <mat-select [(ngModel)]="selectedRoleIds" name="roleIds" multiple cdkFocusInitial>
          <mat-option *ngFor="let role of data.roles" [value]="role.id">{{ role.name }}</mat-option>
        </mat-select>
      </mat-form-field>

      <ng-container *ngIf="data.mode === 'result'">
        <p>The temporary password has been set. Please share it securely with the user:</p>
        <div class="temp-password">{{ data.tempPassword }}</div>
        <p class="hint">The user should change this password on their next login.</p>
      </ng-container>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <ng-container *ngIf="data.mode === 'result'; else editActions">
        <button mat-raised-button color="primary" (click)="dialogRef.close()" cdkFocusInitial>
          Close
        </button>
      </ng-container>
      <ng-template #editActions>
        <button mat-button (click)="dialogRef.close()" [disabled]="saving">{{ 'common.cancel' | transloco }}</button>
        <button mat-raised-button color="primary" (click)="submit()" [disabled]="!valid || saving">
          {{ saving ? 'Saving…' : 'Save' }}
        </button>
      </ng-template>
    </mat-dialog-actions>
  `,
  styles: [`
    .subject { color: var(--text-secondary); margin: 0 0 12px; }
    .full-width { width: 100%; }
    .temp-password {
      background: var(--bg-secondary); padding: 12px 16px; border-radius: 4px;
      font-family: monospace; font-size: 18px; text-align: center;
      margin: 16px 0; user-select: all; letter-spacing: 1px;
    }
    .hint { color: var(--text-secondary); font-size: 13px; }
  `]
})
export class UserEditDialogComponent {
  readonly dialogRef = inject<MatDialogRef<UserEditDialogComponent, true | undefined>>(MatDialogRef);
  readonly data = inject<UserEditDialogData>(MAT_DIALOG_DATA);
  private toast = inject(ToastService);
  private cdr = inject(ChangeDetectorRef);

  newUsername = this.data.mode === 'username' ? this.data.username : '';
  newPassword = '';
  selectedRoleIds: string[] = [...(this.data.selectedRoleIds ?? [])];
  saving = false;

  get title(): string {
    switch (this.data.mode) {
      case 'username': return 'Change Username';
      case 'password': return 'Change Password';
      case 'roles': return 'Change Roles';
      default: return 'Password Reset';
    }
  }

  get valid(): boolean {
    switch (this.data.mode) {
      case 'username': return !!this.newUsername.trim();
      case 'password': return this.newPassword.length >= 6;
      case 'roles': return this.selectedRoleIds.length > 0;
      default: return true;
    }
  }

  submit(): void {
    if (!this.valid || this.saving || !this.data.save) return;
    const value =
      this.data.mode === 'username' ? this.newUsername.trim() :
      this.data.mode === 'password' ? this.newPassword :
      this.selectedRoleIds;

    this.saving = true;
    this.data.save(value as never).subscribe({
      next: () => {
        this.saving = false;
        this.dialogRef.close(true);
      },
      // Stay open so the user keeps what they typed and can correct it.
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'Save failed');
        this.cdr.detectChanges();
      }
    });
  }
}
