import { Signal, computed, signal } from '@angular/core';

/**
 * Callbacks a `ConfirmableAction`'s `mutate` function must invoke once the
 * underlying mutation settles -- deliberately the same `{ onSuccess, onError }`
 * shape `injectMutation`'s own `.mutate(variables, options)` accepts, so a caller
 * typically just forwards this straight through (e.g.
 * `mutate: (customer, callbacks) => this.hardDeleteMutation.mutate(customer.id, callbacks)`).
 */
export interface ConfirmableActionCallbacks<TError> {
  onSuccess: () => void;
  onError: (error: TError) => void;
}

export interface ConfirmableActionOptions<T, TError> {
  /** Fires the underlying mutation for `item`, forwarding `callbacks` to it. */
  mutate: (item: T, callbacks: ConfirmableActionCallbacks<TError>) => void;
  /** The dialog's message when there is no error from a previous failed attempt. */
  defaultMessage: string;
  /** Maps a mutation error to the plain string shown in the dialog in place of `defaultMessage`. */
  toErrorMessage: (error: TError) => string;
  /** Runs after a successful confirm, once the dialog has already closed (e.g. a success toast). */
  onSuccess?: (item: T) => void;
}

/**
 * The `open`/`cancel`/`confirm` + `current`/`dialogMessage` shape shared by every
 * `ConfirmDialog`-backed row action (Delete, Deactivate, Erase) -- extracted per
 * spec-3-5's Scope decision 5 once a third instance (Erase) needed the identical
 * shape Story 3.4's Design Notes flagged as the trigger to revisit. Deliberately a
 * plain signal-returning factory rather than a component/directive (mirrors
 * `FocusTrap`'s "plain TS" precedent) -- it adds a shared *mechanism* only; each
 * caller still supplies its own mutation, default message, error mapping, and
 * success side effect (SRP/DRY without becoming a shared policy).
 */
export interface ConfirmableAction<T> {
  /** The item currently open for confirmation, or `null` when the dialog is closed. */
  readonly current: Signal<T | null>;
  /** `defaultMessage`, or the mapped error message from the previous failed confirm. */
  readonly dialogMessage: Signal<string>;
  /** Opens the dialog for `item`, clearing any error left over from a previous attempt. */
  open(item: T): void;
  /** Closes the dialog without mutating, clearing any error. */
  cancel(): void;
  /** Fires the mutation for the current item; no-ops if nothing is open. */
  confirm(): void;
}

export function createConfirmableAction<T, TError>(
  options: ConfirmableActionOptions<T, TError>,
): ConfirmableAction<T> {
  const current = signal<T | null>(null);
  const errorMessage = signal<string | null>(null);

  const dialogMessage = computed(() => errorMessage() ?? options.defaultMessage);

  function open(item: T): void {
    errorMessage.set(null);
    current.set(item);
  }

  function cancel(): void {
    current.set(null);
    errorMessage.set(null);
  }

  function confirm(): void {
    const item = current();
    if (!item) {
      return;
    }

    options.mutate(item, {
      onSuccess: () => {
        current.set(null);
        errorMessage.set(null);
        options.onSuccess?.(item);
      },
      onError: (error) => {
        errorMessage.set(options.toErrorMessage(error));
      },
    });
  }

  return { current, dialogMessage, open, cancel, confirm };
}
