import { Component, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ToastService } from '@core/services/toast.service';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { AuthService } from '@core/services/auth.service';
import { TranslocoService } from '@jsverse/transloco';

@Component({
  standalone: false,
  selector: 'app-login',
  template: `
    <div class="login-container">
      <mat-card class="login-card">
        <mat-card-header>
          <mat-card-title>{{ 'app.name' | transloco }}</mat-card-title>
          <mat-card-subtitle>{{ 'auth.signInSubtitle' | transloco }}</mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          <form [formGroup]="loginForm" (ngSubmit)="onSubmit()">
            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'auth.username' | transloco }}</mat-label>
              <input matInput formControlName="username" autocomplete="username">
              <mat-icon matSuffix>person</mat-icon>
              <mat-error *ngIf="loginForm.get('username')?.hasError('required')">
                {{ 'auth.usernameRequired' | transloco }}
              </mat-error>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'auth.password' | transloco }}</mat-label>
              <input matInput [type]="hidePassword ? 'password' : 'text'"
                     formControlName="password" autocomplete="current-password">
              <button mat-icon-button matSuffix type="button"
                      (click)="hidePassword = !hidePassword"
                      [attr.aria-label]="(hidePassword ? 'auth.showPassword' : 'auth.hidePassword') | transloco"
                      [attr.aria-pressed]="!hidePassword">
                <mat-icon>{{hidePassword ? 'visibility_off' : 'visibility'}}</mat-icon>
              </button>
              <mat-error *ngIf="loginForm.get('password')?.hasError('required')">
                {{ 'auth.passwordRequired' | transloco }}
              </mat-error>
            </mat-form-field>

            <button mat-raised-button color="primary" class="full-width"
                    type="submit" [disabled]="loading || loginForm.invalid">
              <mat-spinner *ngIf="loading" diameter="20" class="inline-spinner"></mat-spinner>
              <span *ngIf="!loading">{{ 'auth.signIn' | transloco }}</span>
            </button>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .login-container {
      display: flex;
      justify-content: center;
      align-items: center;
      /* min-height, not height: with a fixed height the card clips instead of
         scrolling once validation errors expand it on a short window. */
      min-height: 100vh;
      padding: 24px 16px;
      box-sizing: border-box;
      background: var(--bg-primary);
    }
    .login-card {
      width: 400px;
      max-width: calc(100vw - 32px);
      padding: 24px;
    }
    mat-form-field {
      margin-bottom: 8px;
    }
    .inline-spinner {
      display: inline-block;
    }
  `]
})
export class LoginComponent {
  loginForm: FormGroup;
  hidePassword = true;
  loading = false;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router,
    private toast: ToastService,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef
  ) {
    this.loginForm = this.fb.group({
      username: ['', Validators.required],
      password: ['', Validators.required]
    });
  }

  onSubmit(): void {
    if (this.loginForm.invalid) return;

    this.loading = true;
    this.authService.login(this.loginForm.value).pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: this.transloco.translate('auth.loginTimedOut') } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: () => {
        this.loading = false;
        this.cdr.detectChanges();
        this.router.navigate([this.authService.landingRoute()]);
      },
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'auth.loginFailed');
        this.cdr.detectChanges();
      }
    });
  }
}
