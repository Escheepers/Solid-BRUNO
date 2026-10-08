import { createConfirmableAction } from './confirmable-action';

interface Item {
  id: string;
}

interface FakeError {
  detail: string;
}

describe('createConfirmableAction', () => {
  function setUp(mutateImpl?: (item: Item, callbacks: { onSuccess: () => void; onError: (e: FakeError) => void }) => void) {
    const mutate = vi.fn(mutateImpl ?? (() => {}));
    const onSuccess = vi.fn();
    const toErrorMessage = vi.fn((error: FakeError) => error.detail);

    const action = createConfirmableAction<Item, FakeError>({
      mutate,
      defaultMessage: 'Default message.',
      toErrorMessage,
      onSuccess,
    });

    return { action, mutate, onSuccess, toErrorMessage };
  }

  it('starts closed with no current item', () => {
    const { action } = setUp();

    expect(action.current()).toBeNull();
  });

  describe('open', () => {
    it('sets current to the opened item and dialogMessage to defaultMessage', () => {
      const { action } = setUp();

      action.open({ id: 'a1' });

      expect(action.current()).toEqual({ id: 'a1' });
      expect(action.dialogMessage()).toBe('Default message.');
    });

    it('clears a previous error message when reopened for a new item', () => {
      const { action } = setUp((item, callbacks) => callbacks.onError({ detail: 'boom' }));

      action.open({ id: 'a1' });
      action.confirm();
      expect(action.dialogMessage()).toBe('boom');

      action.open({ id: 'a2' });

      expect(action.dialogMessage()).toBe('Default message.');
    });
  });

  describe('hasError', () => {
    it('is false until a confirm fails, true while the error is showing, and false again after cancel or reopen', () => {
      const { action } = setUp((item, callbacks) => callbacks.onError({ detail: 'boom' }));

      expect(action.hasError()).toBe(false);

      action.open({ id: 'a1' });
      expect(action.hasError()).toBe(false);

      action.confirm();
      expect(action.hasError()).toBe(true);

      action.cancel();
      expect(action.hasError()).toBe(false);

      action.open({ id: 'a2' });
      action.confirm();
      expect(action.hasError()).toBe(true);
      action.open({ id: 'a3' });
      expect(action.hasError()).toBe(false);
    });

    it('stays false after a successful confirm', () => {
      const { action } = setUp((item, callbacks) => callbacks.onSuccess());

      action.open({ id: 'a1' });
      action.confirm();

      expect(action.hasError()).toBe(false);
    });
  });

  describe('cancel', () => {
    it('closes the dialog and never calls mutate', () => {
      const { action, mutate } = setUp();

      action.open({ id: 'a1' });
      action.cancel();

      expect(action.current()).toBeNull();
      expect(mutate).not.toHaveBeenCalled();
    });

    it('clears any error message left over from a previous failed confirm', () => {
      const { action } = setUp((item, callbacks) => callbacks.onError({ detail: 'boom' }));

      action.open({ id: 'a1' });
      action.confirm();
      action.cancel();

      expect(action.dialogMessage()).toBe('Default message.');
    });
  });

  describe('confirm', () => {
    it('does nothing when no item is open', () => {
      const { action, mutate } = setUp();

      action.confirm();

      expect(mutate).not.toHaveBeenCalled();
    });

    it('calls mutate with the current item', () => {
      const { action, mutate } = setUp();

      action.open({ id: 'a1' });
      action.confirm();

      expect(mutate).toHaveBeenCalledWith({ id: 'a1' }, expect.any(Object));
    });

    it('on success, closes the dialog, clears the error, and calls the onSuccess callback with the item', () => {
      const { action, onSuccess } = setUp((item, callbacks) => callbacks.onSuccess());

      action.open({ id: 'a1' });
      action.confirm();

      expect(action.current()).toBeNull();
      expect(action.dialogMessage()).toBe('Default message.');
      expect(onSuccess).toHaveBeenCalledWith({ id: 'a1' });
    });

    it('on failure, keeps the dialog open showing the mapped error message instead of the default', () => {
      const { action, toErrorMessage } = setUp((item, callbacks) =>
        callbacks.onError({ detail: 'This customer has bookings.' }),
      );

      action.open({ id: 'a1' });
      action.confirm();

      expect(action.current()).toEqual({ id: 'a1' });
      expect(action.dialogMessage()).toBe('This customer has bookings.');
      expect(toErrorMessage).toHaveBeenCalledWith({ detail: 'This customer has bookings.' });
    });
  });
});
