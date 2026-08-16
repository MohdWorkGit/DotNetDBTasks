import { Component, ChangeDetectorRef, OnInit } from '@angular/core';
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
          <div *ngIf="ssoProbing" class="sso-probe">
            <mat-spinner diameter="36"></mat-spinner>
            <span class="sso-probe-label">{{ 'auth.ssoChecking' | transloco }}</span>
          </div>

          <form *ngIf="!ssoProbing" [formGroup]="loginForm" (ngSubmit)="onSubmit()">
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
    .sso-probe {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      /* Roughly the height of the form it stands in for, so the card doesn't jump
         when the probe gives up and the fields appear. */
      padding: 48px 0;
    }
    .sso-probe-label {
      color: var(--text-secondary);
      font-size: 13px;
      text-align: center;
    }
  `]
})
export class LoginComponent implements OnInit {
  loginForm: FormGroup;
  hidePassword = true;
  loading = false;

  /** Hides the form while the silent Windows sign-in attempt is in flight. */
  ssoProbing = false;

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

  /**
   * Tries the user's Windows identity before showing the form. On a domain-joined machine
   * this signs them in without a click; anywhere else it fails and the form appears.
   *
   * The failure path is deliberately silent — this is something the page tried on the user's
   * behalf, not something they asked for, so a toast would only be noise to the majority of
   * people who are about to type a password anyway.
   */
  ngOnInit(): void {
    // Suppressed by a sign-out (otherwise logging out would sign you straight back in), and
    // pointless when a session is already established.
    if (this.authService.isSsoSuppressed() || this.authService.getAccessToken()) return;

    this.ssoProbing = true;
    this.authService.sso().pipe(
      // Shorter than the form's 30s: nobody should watch a spinner for half a minute before
      // being allowed to type. A browser with no ticket to offer answers well inside this.
      timeout(10000)
    ).subscribe({
      next: () => {
        // Same ordering as onSubmit: landingRoute() reads the cached permission set, so the
        // permissions have to arrive before the redirect.
        this.authService.loadPermissions().subscribe(() =>
          this.router.navigate([this.authService.landingRoute()]));
      },
      error: (err) => {
        // 404 means SSO is switched off server-side and a timeout means something is wrong
        // upstream — in both cases re-probing on every visit to this page is wasted. A 401
        // is left un-suppressed: it just means no ticket was on offer this time.
        if (err?.status === 404 || err?.name === 'TimeoutError') {
          this.authService.suppressSso();
        }
        this.ssoProbing = false;
        this.cdr.detectChanges();
      }
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
        // Permissions decide where they land, so they have to arrive before the redirect.
        this.authService.loadPermissions().subscribe(() =>
          this.router.navigate([this.authService.landingRoute()]));
      },
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'auth.loginFailed');
        this.cdr.detectChanges();
      }
    });
  }
}
