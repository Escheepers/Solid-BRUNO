import { HttpInterceptorFn } from '@angular/common/http';

import { environment } from '../../../environments/environment';

/**
 * Attaches `X-Api-Key` (sourced from the environment config — never hardcoded inline)
 * to every outgoing request, per ARCHITECTURE-SPINE.md's `ApiClient` contract.
 */
export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  const withApiKey = req.clone({
    setHeaders: { 'X-Api-Key': environment.apiKey },
  });

  return next(withApiKey);
};
