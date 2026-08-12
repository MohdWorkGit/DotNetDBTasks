import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { LanguageService } from '../services/language.service';

/**
 * Tags every API call with the active locale so server-generated text — validation failures,
 * permission denials, AD import summaries — comes back translated. Those messages are shown
 * verbatim by ToastService, so without this header an Arabic UI would surface English errors.
 *
 * The .NET side reads it via AcceptLanguageHeaderRequestCultureProvider (see Program.cs).
 */
export const languageInterceptor: HttpInterceptorFn = (request, next) => {
  const language = inject(LanguageService);

  return next(request.clone({
    setHeaders: { 'Accept-Language': language.acceptLanguageHeader() }
  }));
};
