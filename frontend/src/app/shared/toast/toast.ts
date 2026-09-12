import { Component, inject } from '@angular/core';

import { ToastService } from './toast.service';

/**
 * Renders `ToastService`'s current queue (`{components.toast}` in DESIGN.md).
 * Mounted once, app-wide, in `app.html`. Only knows how to display/dismiss what the
 * service hands it — never anything about which feature raised a given message
 * (SRP).
 */
@Component({
  selector: 'app-toast',
  templateUrl: './toast.html',
})
export class Toast {
  protected readonly toastService = inject(ToastService);

  protected dismiss(id: number): void {
    this.toastService.dismiss(id);
  }
}
