import { Component } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { AuthService } from './core/services/auth.service';
import { BrandingService } from './core/services/branding.service';
import { ThemeService } from './core/services/theme.service';
import { LogoUploadDialogComponent } from './shared/components/logo-upload-dialog.component';

@Component({
  standalone: false,
  selector: 'app-root',
  template: `
    <a class="skip-link" href="#main-content">Skip to main content</a>

    <nav aria-label="Main" *ngIf="authService.isAuthenticated$ | async">
      <mat-toolbar color="primary">
        <!-- Falls back to the name whenever no logo is set, so the banner is never blank. -->
        <img *ngIf="brandingService.logoUrl$ | async as logoUrl; else siteName"
             [src]="logoUrl" class="brand-logo" alt="DotNetDBTasks">
        <ng-template #siteName><span>DotNetDBTasks</span></ng-template>
        <span class="spacer"></span>

        <button mat-button routerLink="/user/queries" routerLinkActive="nav-active"
                matTooltip="My Queries" aria-label="My Queries">
          <mat-icon>list</mat-icon> <span class="nav-label">My Queries</span>
        </button>
        <button mat-button routerLink="/user/history" routerLinkActive="nav-active"
                matTooltip="History" aria-label="History">
          <mat-icon>history</mat-icon> <span class="nav-label">History</span>
        </button>
        <button mat-button routerLink="/user/schedules" routerLinkActive="nav-active"
                *ngIf="!authService.isAdminOrAuditor()"
                matTooltip="Schedules" aria-label="Schedules">
          <mat-icon>schedule</mat-icon> <span class="nav-label">Schedules</span>
        </button>

        <button mat-button routerLink="/admin/scheduled-tasks" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAuditor()"
                matTooltip="Schedules" aria-label="Schedules">
          <mat-icon>schedule</mat-icon> <span class="nav-label">Schedules</span>
        </button>
        <button mat-button routerLink="/admin/queries" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAccessManager()"
                matTooltip="Manage Queries" aria-label="Manage Queries">
          <mat-icon>dashboard</mat-icon> <span class="nav-label">Manage Queries</span>
        </button>
        <button mat-button routerLink="/admin/query-groups" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAccessManager()"
                matTooltip="Query Groups" aria-label="Query Groups">
          <mat-icon>folder</mat-icon> <span class="nav-label">Query Groups</span>
        </button>
        <button mat-button routerLink="/admin/users" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAccessManager()"
                matTooltip="Users" aria-label="Users">
          <mat-icon>people</mat-icon> <span class="nav-label">Users</span>
        </button>
        <button mat-button routerLink="/admin/database-users" routerLinkActive="nav-active"
                *ngIf="authService.isAdmin()"
                matTooltip="DB Users" aria-label="DB Users">
          <mat-icon>storage</mat-icon> <span class="nav-label">DB Users</span>
        </button>
        <button mat-button routerLink="/admin/ad-users" routerLinkActive="nav-active"
                *ngIf="authService.isAdmin()"
                matTooltip="AD Users" aria-label="AD Users">
          <mat-icon>group</mat-icon> <span class="nav-label">AD Users</span>
        </button>
        <button mat-button routerLink="/admin/logs" routerLinkActive="nav-active"
                *ngIf="authService.isAdminOrAuditor()"
                matTooltip="Logs" aria-label="Logs">
          <mat-icon>receipt_long</mat-icon> <span class="nav-label">Logs</span>
        </button>

        <button mat-icon-button (click)="themeService.toggle()"
                [matTooltip]="themeToggleLabel | async"
                [attr.aria-label]="themeToggleLabel | async">
          <mat-icon>{{ (themeService.isDarkMode$ | async) ? 'light_mode' : 'dark_mode' }}</mat-icon>
        </button>

        <button mat-icon-button [matMenuTriggerFor]="userMenu"
                matTooltip="Account"
                [attr.aria-label]="'Account menu for ' + authService.getUsername()">
          <mat-icon>account_circle</mat-icon>
        </button>
        <mat-menu #userMenu="matMenu">
          <div mat-menu-item disabled>{{ authService.getUsername() }}</div>
          <button mat-menu-item *ngIf="authService.isAdmin()" (click)="openLogoDialog()">
            <mat-icon>image</mat-icon> Website logo
          </button>
          <button mat-menu-item (click)="authService.logout()">
            <mat-icon>exit_to_app</mat-icon> Logout
          </button>
        </mat-menu>
      </mat-toolbar>
    </nav>

    <!-- Theme toggle for login page (when not authenticated) -->
    <button *ngIf="!(authService.isAuthenticated$ | async)"
            mat-icon-button class="login-theme-toggle"
            (click)="themeService.toggle()"
            [matTooltip]="themeToggleLabel | async"
            [attr.aria-label]="themeToggleLabel | async">
      <mat-icon>{{ (themeService.isDarkMode$ | async) ? 'light_mode' : 'dark_mode' }}</mat-icon>
    </button>

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
    .nav-label { margin-left: 4px; }
    /* An Admin sees 9 nav buttons plus theme and account. On a 1366px laptop the
       row overflows and pushes the account menu off-screen, so below 1400px the
       labels collapse and the tooltip + aria-label carry the name. */
    @media (max-width: 1400px) {
      .nav-label { display: none; }
      mat-toolbar button { margin: 0 2px; }
      /* The nav needs the room more than the logo does once labels collapse. */
      .brand-logo { max-width: 140px; }
    }
    .login-theme-toggle {
      position: fixed;
      top: 16px;
      right: 16px;
      z-index: 100;
      color: var(--text-secondary);
    }
  `]
})
export class AppComponent {
  /** Serves both the tooltip and the accessible name of the theme toggle. */
  readonly themeToggleLabel: Observable<string>;

  constructor(
    public authService: AuthService,
    public themeService: ThemeService,
    public brandingService: BrandingService,
    private dialog: MatDialog
  ) {
    this.themeToggleLabel = this.themeService.isDarkMode$.pipe(
      map(dark => dark ? 'Switch to light mode' : 'Switch to dark mode')
    );

    // The info endpoint needs a token, so wait for sign-in rather than firing at startup —
    // and re-fetch on each sign-in so a logo changed by another admin shows up.
    this.authService.isAuthenticated$.subscribe(authenticated => {
      if (authenticated) this.brandingService.refresh();
    });
  }

  openLogoDialog(): void {
    this.dialog.open(LogoUploadDialogComponent, {
      width: '520px',
      autoFocus: 'dialog',
      ariaModal: true
    });
  }
}
