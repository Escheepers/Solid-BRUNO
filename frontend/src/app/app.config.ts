import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { routes } from './app.routes';
import { apiKeyInterceptor } from './core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from './core/api-client/error-normalization.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
    provideTanStackQuery(new QueryClient()),
  ],
};
