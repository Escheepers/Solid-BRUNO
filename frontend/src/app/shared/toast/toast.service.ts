import { Injectable, signal } from '@angular/core';

export interface ToastMessage {
  id: number;
  message: string;
}

const AUTO_DISMISS_MS = 4000;

/**
 * A signal-based queue of active toast messages (`{components.toast}` in
 * DESIGN.md). Only responsible for queuing/auto-dismissing/manually-dismissing
 * messages — it has no idea what triggered a given toast (SRP; that's each
 * feature's concern via `success(...)`). Per `EXPERIENCE.md`'s State Patterns, a
 * toast is reserved for success confirmations only — there is deliberately no
 * `error`/`warning` variant here.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly messages = signal<ToastMessage[]>([]);
  readonly toasts = this.messages.asReadonly();

  private nextId = 0;

  success(message: string): void {
    const id = this.nextId++;
    this.messages.update((current) => [...current, { id, message }]);

    setTimeout(() => this.dismiss(id), AUTO_DISMISS_MS);
  }

  dismiss(id: number): void {
    this.messages.update((current) => current.filter((toast) => toast.id !== id));
  }
}
