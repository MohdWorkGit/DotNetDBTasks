import { Component } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { combineLatest, Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { TranslocoService } from '@jsverse/transloco';
import { AuthService } from './core/services/auth.service';
import { BrandingService } from './core/services/branding.service';
import { ThemeService } from './core/services/theme.service';
import { LanguageService } from './core/services/language.service';
import { LOCALE_LABELS } from './core/models/locale';
import { LogoUploadDialogComponent } from './shared/components/logo-upload-dialog.component';

@Component({
  standalone: false,
  selector: 'app-root',
  template: `
    <a class="skip-link" href="#main-content">{{ 'app.skipToContent' | transloco }}</a>

    <nav [attr.aria-label]="'app.mainNav' | transloco" *ngIf="authService.isAuthenticated$ | async">
      <mat-toolbar color="primary">
        <!-- Falls back to the name whenever no logo is set, so the banner is never blank. -->
        <img *ngIf="brandingService.logoUrl$ | async as logoUrl; else siteName"
             [src]="logoUrl" class="brand-logo" alt="DotNetDBTasks">
        <ng-template #siteName><span>DotNetDBTasks</span></ng-template>
        <span class="spacer"></span>

        <button mat-button routerLink="/user/queries" routerLinkActive="nav-active"
                [matTooltip]="'nav.myQueries' | transloco" [attr.aria-label]="'nav.myQueries' | transloco">
          <mat-icon>list</mat-icon> <span class="nav-label">{{ 'nav.myQueries' | transloco }}</span>
        </button>
        <button mat-button routerLink="/user/history" routerLinkActive="nav-active"
                [matTooltip]="'nav.history' | transloco" [attr.aria-label]="'nav.history' | transloco">
          <mat-icon>history</mat-icon> <span class="nav-label">{{ 'nav.history' | transloco }}</span>
        </button>
        <button mat-button routerLink="/user/schedules" routerLinkActive="nav-active"
                *ngIf="!authService.isAdminOrAuditor()"
                [matTooltip]="'nav.schedules' | transloco" [attr.aria-label]="'nav.schedules' | transloco">
          <mat-icon>schedule</mat-icon> <span class="nav-label">{{ 'nav.schedules' | transloco }}</span>
        </button>

        <button mat-button routerLink="/admin/scheduled-tasks" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAuditor()"
                [matTooltip]="'nav.schedules' | transloco" [attr.aria-label]="'nav.schedules' | transloco">
          <mat-icon>schedule</mat-icon> <span class="nav-label">{{ 'nav.schedules' | transloco }}</span>
        </button>
        <button mat-button routerLink="/admin/queries" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAccessManager()"
                [matTooltip]="'nav.manageQueries' | transloco" [attr.aria-label]="'nav.manageQueries' | transloco">
          <mat-icon>dashboard</mat-icon> <span class="nav-label">{{ 'nav.manageQueries' | transloco }}</span>
        </button>
        <button mat-button routerLink="/admin/query-groups" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAccessManager()"
                [matTooltip]="'nav.queryGroups' | transloco" [attr.aria-label]="'nav.queryGroups' | transloco">
          <mat-icon>folder</mat-icon> <span class="nav-label">{{ 'nav.queryGroups' | transloco }}</span>
        </button>
        <button mat-button routerLink="/admin/users" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAccessManager()"
                [matTooltip]="'nav.users' | transloco" [attr.aria-label]="'nav.users' | transloco">
          <mat-icon>people</mat-icon> <span class="nav-label">{{ 'nav.users' | transloco }}</span>
        </button>
        <button mat-button routerLink="/admin/database-users" routerLinkActive="nav-active"
                *ngIf="authService.isAdmin()"
                [matTooltip]="'nav.dbUsers' | transloco" [attr.aria-label]="'nav.dbUsers' | transloco">
          <mat-icon>storage</mat-icon> <span class="nav-label">{{ 'nav.dbUsers' | transloco }}</span>
        </button>
        <button mat-button routerLink="/admin/ad-users" routerLinkActive="nav-active"
                *ngIf="authService.isAdmin()"
                [matTooltip]="'nav.adUsers' | transloco" [attr.aria-label]="'nav.adUsers' | transloco">
          <mat-icon>group</mat-icon> <span class="nav-label">{{ 'nav.adUsers' | transloco }}</span>
        </button>
        <button mat-button routerLink="/admin/logs" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAuditor()"
                [matTooltip]="'nav.logs' | transloco" [attr.aria-label]="'nav.logs' | transloco">
          <mat-icon>receipt_long</mat-icon> <span class="nav-label">{{ 'nav.logs' | transloco }}</span>
        </button>

        <button mat-icon-button (click)="toggleLanguage()"
                [matTooltip]="languageToggleLabel | async"
                [attr.aria-label]="languageToggleLabel | async">
          <mat-icon>translate</mat-icon>
        </button>

        <button mat-icon-button (click)="themeService.toggle()"
                [matTooltip]="themeToggleLabel | async"
                [attr.aria-label]="themeToggleLabel | async">
          <mat-icon>{{ (themeService.isDarkMode$ | async) ? 'light_mode' : 'dark_mode' }}</mat-icon>
        </button>

        <button mat-icon-button [matMenuTriggerFor]="userMenu"
                [matTooltip]="'nav.account' | transloco"
                [attr.aria-label]="'nav.accountMenuFor' | transloco: { username: authService.getUsername() }">
          <mat-icon>account_circle</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu">
          <div mat-menu-item disabled>{{ authService.getUsername() }}</div>
          <button mat-menu-item *ngIf="authService.isAdmin()" (click)="openLogoDialog()">
            <mat-icon>image</mat-icon> {{ 'nav.websiteLogo' | transloco }}
          </button>
          <button mat-menu-item (click)="authService.logout()">
            <mat-icon>exit_to_app</mat-icon> {{ 'nav.logout' | transloco }}
          </button>
        </mat-menu>
      </mat-toolbar>
    </nav>

    <!-- Theme and language toggles for the login page (when not authenticated). The language
         one matters most here: someone who cannot read the English form needs to switch
         before signing in, not after. -->
    <div *ngIf="!(authService.isAuthenticated$ | async)" class="login-toggles">
      <button mat-icon-button
              (click)="toggleLanguage()"
              [matTooltip]="languageToggleLabel | async"
              [attr.aria-label]="languageToggleLabel | async">
        <mat-icon>translate</mat-icon>
      </button>
      <button mat-icon-button
              (click)="themeService.toggle()"
              [matTooltip]="themeToggleLabel | async"
              [attr.aria-label]="themeToggleLabel | async">
        <mat-icon>{{ (themeService.isDarkMode$ | async) ? 'light_mode' : 'dark_mode' }}</mat-icon>
      </button>
    </div>

    <main id="main-content" tabindex="-1">
      <router-outlet></router-outlet>
    </main>
  `,
  styles: [`
    .spacer { flex: 1 1 auto; }
    /* Capped in both directions: an over-tall logo would stretch the toolbar, and an
       over-wide one would push the nav buttons off-screen. object-fit keeps the aspect
       ratio whatever the admin uploads. */
    .brand-logo {
      height: 40px;
      max-width: 200px;
      object-fit: contain;
      display: block;
    }
    mat-toolbar button { margin: 0 4px; }
    .nav-label { margin-inline-start: 4px; }
    /* An Admin sees 9 nav buttons plus theme and account. On a 1366px laptop the
       row overflows and pushes the account menu off-screen, so below 1400px the
       labels collapse and the tooltip + aria-label carry the name. */
    @media (max-width: 1400px) {
      .nav-label { display: none; }
      mat-toolbar button { margin: 0 2px; }
      /* The nav needs the room more than the logo does once labels collapse. */
      .brand-logo { max-width: 140px; }
    }
    /* inset-inline-end, not right: the toggles belong in the trailing corner, which is the
       left-hand side once the page flips to RTL. */
    .login-toggles {
      position: fixed;
      top: 16px;
      inset-inline-end: 16px;
      z-index: 100;
      display: flex;
      gap: 4px;
      color: var(--text-secondary);
    }
  `]
})
export class AppComponent {
  /** Serves both the tooltip and the accessible name of the theme toggle. */
  readonly themeToggleLabel: Observable<string>;

  /** Names the language being switched *to*, in that language — "التبديل إلى العربية". */
  readonly languageToggleLabel: Observable<string>;

  constructor(
    public authService: AuthService,
    public themeService: ThemeService,
    public brandingService: BrandingService,
    public languageService: LanguageService,
    private transloco: TranslocoService,
    private dialog: MatDialog
  ) {
    // Re-derive on language change as well as theme change, otherwise the tooltip keeps the
    // wording of the language you just left.
    this.themeToggleLabel = combineLatest([
      this.themeService.isDarkMode$,
      this.transloco.langChanges$
    ]).pipe(
      map(([dark]) => this.transloco.translate(dark ? 'nav.switchToLight' : 'nav.switchToDark'))
    );

    this.languageToggleLabel = combineLatest([
      this.languageService.activeLocale$,
      this.transloco.langChanges$
    ]).pipe(
      map(() => this.transloco.translate('nav.switchLanguageTo', {
        language: LOCALE_LABELS[this.languageService.other()]
      }))
    );

    // The info endpoint needs a token, so wait for sign-in rather than firing at startup —
    // and re-fetch on each sign-in so a logo changed by another admin shows up.
    this.authService.isAuthenticated$.subscribe(authenticated => {
      if (authenticated) this.brandingService.refresh();
    });
  }

  /**
   * Flips between the two locales.
   *
   * Angular Material reads direction from the document when an overlay is *created*
   * (@angular/cdk/bidi), so any menu, dialog or snackbar already on screen would keep the old
   * direction and render half-mirrored. Closing open overlays first avoids that; components
   * themselves re-render because Transloco is configured with reRenderOnLangChange.
   */
  toggleLanguage(): void {
    this.dialog.closeAll();
    this.languageService.use(this.languageService.other());
  }

  openLogoDialog(): void {
    this.dialog.open(LogoUploadDialogComponent, {
      width: '520px',
      autoFocus: 'dialog',
      ariaModal: true
    });
  }
}
