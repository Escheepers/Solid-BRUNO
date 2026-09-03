import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type ApiClientParams = Record<string, string | number | boolean>;

/**
 * Thin generic wrapper over `HttpClient` for the API's `/api` surface — not a
 * per-endpoint method set. Every request is issued as a relative `/api/...` path so
 * `ng serve`'s dev proxy (`proxy.conf.json`) routes it to the real backend, and every
 * request passes through the app-wide interceptor pipeline (`apiKeyInterceptor`,
 * `errorNormalizationInterceptor`) registered in `app.config.ts`. Concrete feature
 * services should call through this rather than injecting `HttpClient` directly, so
 * the auth-header and error-normalization guarantees stay universal.
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<T>(path: string, params?: ApiClientParams): Observable<T> {
    return this.http.get<T>(this.url(path), { params: this.toHttpParams(params) });
  }

  post<T, B>(path: string, body: B): Observable<T> {
    return this.http.post<T>(this.url(path), body);
  }

  put<T, B>(path: string, body: B): Observable<T> {
    return this.http.put<T>(this.url(path), body);
  }

  delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(this.url(path));
  }

  private url(path: string): string {
    return `/api/${path}`;
  }

  private toHttpParams(params?: ApiClientParams): HttpParams | undefined {
    if (!params) {
      return undefined;
    }

    let httpParams = new HttpParams();
    for (const [key, value] of Object.entries(params)) {
      httpParams = httpParams.set(key, String(value));
    }
    return httpParams;
  }
}
