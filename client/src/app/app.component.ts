import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { Observable } from 'rxjs';
import { map, switchMap } from 'rxjs/operators';
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

        <!-- Grouped into menus rather than a flat row. An Admin has nine destinations; as
             flat buttons they overflowed a 1366px laptop and pushed the account menu off
             screen, which the old max-width:1400px label-collapsing hack only partly hid.
             Each group renders only if the role can reach something inside it, so a plain
             User still sees three plain buttons and no empty dropdowns. -->

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

        <!-- Queries: authoring, group/query accessibility, and the scheduled runs of those
             queries. The trigger also shows for an Auditor, who reaches nothing here except
             the read-only scheduled tasks — without that they would lose the entry entirely
             when it moved out of the audit menu. -->
        <button mat-button *ngIf="authService.isAdminOrAccessManager() || authService.isAdminOrAuditor()"
                [matMenuTriggerFor]="queriesMenu"
                [class.nav-active]="inSection(['/admin/queries', '/admin/query-groups', '/admin/scheduled-tasks'])"
                [matTooltip]="'nav.queriesGroup' | transloco">
          <mat-icon>dashboard</mat-icon>
          <span class="nav-label">{{ 'nav.queriesGroup' | transloco }}</span>
          <mat-icon iconPositionEnd>arrow_drop_down</mat-icon>
        </button>
        <mat-menu #queriesMenu="matMenu">
          <button mat-menu-item *ngIf="authService.isAdminOrAccessManager()" routerLink="/admin/queries">
            <mat-icon>dashboard</mat-icon> {{ 'nav.manageQueries' | transloco }}
          </button>
          <button mat-menu-item *ngIf="authService.isAdminOrAccessManager()" routerLink="/admin/query-groups">
            <mat-icon>folder</mat-icon> {{ 'nav.queryGroups' | transloco }}
          </button>
          <button mat-menu-item *ngIf="authService.isAdminOrAuditor()" routerLink="/admin/scheduled-tasks">
            <mat-icon>schedule</mat-icon> {{ 'nav.schedules' | transloco }}
          </button>
        </mat-menu>

        <!-- People and the connections their queries run through. -->
        <button mat-button *ngIf="authService.isAdminOrAccessManager()"
                [matMenuTriggerFor]="peopleMenu"
                [class.nav-active]="inSection(['/admin/users', '/admin/ad-users', '/admin/database-users'])"
                [matTooltip]="'nav.peopleGroup' | transloco">
          <mat-icon>people</mat-icon>
          <span class="nav-label">{{ 'nav.peopleGroup' | transloco }}</span>
          <mat-icon iconPositionEnd>arrow_drop_down</mat-icon>
        </button>
        <mat-menu #peopleMenu="matMenu">
          <button mat-menu-item routerLink="/admin/users">
            <mat-icon>people</mat-icon> {{ 'nav.users' | transloco }}
          </button>
          <button mat-menu-item *ngIf="authService.isAdmin()" routerLink="/admin/ad-users">
            <mat-icon>group</mat-icon> {{ 'nav.adUsers' | transloco }}
          </button>
          <button mat-menu-item *ngIf="authService.isAdmin()" routerLink="/admin/database-users">
            <mat-icon>storage</mat-icon> {{ 'nav.dbUsers' | transloco }}
          </button>
        </mat-menu>

        <!-- Audit: the two read-only trails. -->
        <button mat-button *ngIf="authService.isAdminOrAuditor()"
                [matMenuTriggerFor]="auditMenu"
                [class.nav-active]="inSection(['/admin/logs', '/admin/system-audit'])"
                [matTooltip]="'nav.auditGroup' | transloco">
          <mat-icon>fact_check</mat-icon>
          <span class="nav-label">{{ 'nav.auditGroup' | transloco }}</span>
          <mat-icon iconPositionEnd>arrow_drop_down</mat-icon>
        </button>
        <mat-menu #auditMenu="matMenu">
          <button mat-menu-item routerLink="/admin/logs">
            <mat-icon>receipt_long</mat-icon> {{ 'nav.logs' | transloco }}
          </button>
          <button mat-menu-item routerLink="/admin/system-audit">
            <mat-icon>fact_check</mat-icon> {{ 'nav.systemAudit' | transloco }}
          </button>
        </mat-menu>

        <button mat-icon-button [matMenuTriggerFor]="userMenu"
                [matTooltip]="'nav.account' | transloco"
                [attr.aria-label]="'nav.accountMenuFor' | transloco: { username: authService.getUsername() }">
          <mat-icon>account_circle</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu">
          <div mat-menu-item disabled>{{ authService.getUsername() }}</div>

          <mat-divider></mat-divider>

          <!-- Language reloads the page (see LanguageService.use), so there is nothing to
               keep open. Theme changes live, and stopPropagation keeps the menu open so it
               can be toggled back and forth without reopening. -->
          <button mat-menu-item (click)="toggleLanguage()"
                  [attr.aria-label]="languageToggleLabel | async">
            <mat-icon>translate</mat-icon>
            <span>{{ languageToggleLabel | async }}</span>
          </button>
          <button mat-menu-item (click)="$event.stopPropagation(); themeService.toggle()"
                  [attr.aria-label]="themeToggleLabel | async">
            <mat-icon>{{ (themeService.isDarkMode$ | async) ? 'light_mode' : 'dark_mode' }}</mat-icon>
            <span>{{ themeToggleLabel | async }}</span>
          </button>

          <mat-divider></mat-divider>

          <button mat-menu-item *ngIf="authService.isAdmin()" routerLink="/admin/settings">
            <mat-icon>settings</mat-icon> {{ 'nav.settings' | transloco }}
          </button>
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
    /* Grouping the destinations into menus cut an Admin from nine nav buttons to four,
       so labels now fit a 1366px laptop and the old 1400px collapse point only made the
       bar cryptic. Kept at a genuinely narrow width, where the tooltip and aria-label
       still carry the name. */
    @media (max-width: 1100px) {
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
    private router: Router,
    private transloco: TranslocoService,
    private dialog: MatDialog
  ) {
    // selectTranslate, not translate(): LanguageService applies the stored preference in
    // its own constructor, which runs before this one, so the langChanged event has already
    // fired by the time we could subscribe to it — a plain translate() here returns the raw
    // key and never re-runs. selectTranslate waits for the catalog and re-emits on switch.
    this.themeToggleLabel = this.themeService.isDarkMode$.pipe(
      switchMap(dark =>
        this.transloco.selectTranslate(dark ? 'nav.switchToLight' : 'nav.switchToDark'))
    );

    this.languageToggleLabel = this.languageService.activeLocale$.pipe(
      switchMap(() => this.transloco.selectTranslate('nav.switchLanguageTo', {
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
   * Whether the current route sits under one of a menu's destinations.
   *
   * <p>routerLinkActive cannot do this: it lives on the menu *items*, which are inside an
   * overlay that is not rendered until the menu opens — so the trigger would never light up
   * and there would be no indication of which section you are in.</p>
   */
  inSection(prefixes: string[]): boolean {
    const url = this.router.url;
    return prefixes.some(p => url === p || url.startsWith(p + '/'));
  }

  /** Flips between the two locales. LanguageService reloads the page — see its docs for why. */
  toggleLanguage(): void {
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
