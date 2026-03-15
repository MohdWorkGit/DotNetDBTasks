import { Component } from '@angular/core';
import { AuthService } from './core/services/auth.service';
import { ThemeService } from './core/services/theme.service';

@Component({
  standalone: false,
  selector: 'app-root',
  template: `
    <mat-toolbar color="primary" *ngIf="authService.isAuthenticated$ | async">
      <span>DotNetDBTasks</span>
      <span class="spacer"></span>

      <button mat-button routerLink="/user/queries">
        <mat-icon>list</mat-icon> My Queries
      </button>
      <button mat-button routerLink="/user/history">
        <mat-icon>history</mat-icon> History
      </button>

      <button mat-button routerLink="/admin/queries" *ngIf="authService.isAdminOrAuditor()">
        <mat-icon>dashboard</mat-icon> Manage Queries
      </button>
      <button mat-button routerLink="/admin/users" *ngIf="authService.isAdminOrAuditor()">
        <mat-icon>people</mat-icon> Users
      </button>
      <button mat-button routerLink="/admin/database-users" *ngIf="authService.isAdmin()">
        <mat-icon>storage</mat-icon> DB Users
      </button>
      <button mat-button routerLink="/admin/ad-users" *ngIf="authService.isAdmin()">
        <mat-icon>group</mat-icon> AD Users
      </button>
      <button mat-button routerLink="/admin/logs" *ngIf="authService.isAdminOrAuditor()">
        <mat-icon>receipt_long</mat-icon> Logs
      </button>

      <button mat-icon-button (click)="themeService.toggle()"
              [matTooltip]="(themeService.isDarkMode$ | async) ? 'Switch to light mode' : 'Switch to dark mode'">
        <mat-icon>{{ (themeService.isDarkMode$ | async) ? 'light_mode' : 'dark_mode' }}</mat-icon>
      </button>

      <button mat-icon-button [matMenuTriggerFor]="userMenu">
        <mat-icon>account_circle</mat-icon>
      </button>
      <mat-menu #userMenu="matMenu">
        <div mat-menu-item disabled>{{ authService.getUsername() }}</div>
        <button mat-menu-item (click)="authService.logout()">
          <mat-icon>exit_to_app</mat-icon> Logout
        </button>
      </mat-menu>
    </mat-toolbar>

    <!-- Theme toggle for login page (when not authenticated) -->
    <button *ngIf="!(authService.isAuthenticated$ | async)"
            mat-icon-button class="login-theme-toggle"
            (click)="themeService.toggle()"
            [matTooltip]="(themeService.isDarkMode$ | async) ? 'Switch to light mode' : 'Switch to dark mode'">
      <mat-icon>{{ (themeService.isDarkMode$ | async) ? 'light_mode' : 'dark_mode' }}</mat-icon>
    </button>

    <router-outlet></router-outlet>
  `,
  styles: [`
    .spacer { flex: 1 1 auto; }
    mat-toolbar button { margin: 0 4px; }
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
  constructor(
    public authService: AuthService,
    public themeService: ThemeService
  ) {}
}
