import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { RouterModule } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';

import { provideTransloco, TranslocoModule } from '@jsverse/transloco';

import { AppComponent } from './app.component';
import { jwtInterceptor } from './core/interceptors/jwt.interceptor';
import { languageInterceptor } from './core/interceptors/language.interceptor';
import { authGuard } from './core/guards/auth.guard';
import { noAuthGuard, rootRedirectGuard } from './core/guards/no-auth.guard';
import { TranslocoHttpLoader } from './core/transloco-loader';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { LocalizedPaginatorIntl } from './core/localized-paginator-intl';
import { DEFAULT_LOCALE, LOCALES } from './core/models/locale';
import { environment } from '@env/environment';

const routes = [
  {
    path: '',
    pathMatch: 'full' as const,
    canActivate: [rootRedirectGuard],
    children: []
  },
  {
    path: 'login',
    loadChildren: () => import('./features/auth/auth.module').then(m => m.AuthModule),
    canActivate: [noAuthGuard]
  },
  {
    path: 'admin',
    loadChildren: () => import('./features/admin/admin.module').then(m => m.AdminModule),
    canActivate: [authGuard]
    // No permissions here: /admin is a container, and each child route names its own. A union
    // listed here would be a second place to update every time a permission is added.
  },
  {
    path: 'user',
    loadChildren: () => import('./features/user/user.module').then(m => m.UserModule),
    canActivate: [authGuard]
  },
  { path: '**', redirectTo: '/login' }
];

@NgModule({
  declarations: [AppComponent],
  imports: [
    BrowserModule,
    RouterModule.forRoot(routes),
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatSnackBarModule,
    MatTooltipModule,
    MatDividerModule,
    TranslocoModule
  ],
  providers: [
    provideAnimations(),
    // Material keeps the paginator's labels in a service, out of reach of the
    // transloco pipe; swapping the provider is the supported way to translate them.
    { provide: MatPaginatorIntl, useClass: LocalizedPaginatorIntl },
    // languageInterceptor first: it only stamps a header, and putting it ahead of the JWT
    // interceptor means a request replayed after a token refresh keeps the header too.
    provideHttpClient(withInterceptors([languageInterceptor, jwtInterceptor])),
    provideTransloco({
      config: {
        availableLangs: [...LOCALES],
        defaultLang: DEFAULT_LOCALE,
        // LanguageService owns the active language; it calls setActiveLang() on construction
        // from the stored preference, so Transloco must not re-apply defaultLang on navigation.
        reRenderOnLangChange: true,
        prodMode: environment.production,
        // A missing key should be visible in review, not silently blank.
        missingHandler: { logMissingKey: !environment.production, useFallbackTranslation: true },
        fallbackLang: DEFAULT_LOCALE
      },
      loader: TranslocoHttpLoader
    })
  ],
  bootstrap: [AppComponent]
})
export class AppModule {}
