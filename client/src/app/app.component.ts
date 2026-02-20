import { Component } from '@angular/core';
import { AuthService } from './core/services/auth.service';

@Component({
  selector: 'app-root',
  template: `
    <mat-toolbar color="primary" *ngIf="authService.isAuthenticated$ | async">
      <span>DotNetDBTasks</span>
      <span class="spacer"></span>

      <button mat-button routerLink="/user/queries" *ngIf="!authService.isAdmin()">
        <mat-icon>list</mat-icon> My Queries
      </button>
      <button mat-button routerLink="/user/history" *ngIf="!authService.isAdmin()">
        <mat-icon>history</mat-icon> History
      </button>

      <button mat-button routerLink="/admin/queries" *ngIf="authService.isAdmin()">
        <mat-icon>dashboard</mat-icon> Manage Queries
      </button>
      <button mat-button routerLink="/admin/logs" *ngIf="authService.isAdmin()">
        <mat-icon>receipt_long</mat-icon> Logs
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

    <router-outlet></router-outlet>
  `,
  styles: [`
    .spacer { flex: 1 1 auto; }
    mat-toolbar button { margin: 0 4px; }
  `]
})
export class AppComponent {
  constructor(public authService: AuthService) {}
}
