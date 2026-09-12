import { TestBed } from '@angular/core/testing';

import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ToastService);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('adds a toast when success() is called', () => {
    service.success('Vehicle created.');

    expect(service.toasts().length).toBe(1);
    expect(service.toasts()[0].message).toBe('Vehicle created.');
  });

  it('assigns each toast a distinct id', () => {
    service.success('First');
    service.success('Second');

    const ids = service.toasts().map((toast) => toast.id);
    expect(new Set(ids).size).toBe(2);
  });

  it('auto-removes the toast after its dismiss timeout elapses', () => {
    vi.useFakeTimers();

    service.success('Vehicle created.');
    expect(service.toasts().length).toBe(1);

    vi.advanceTimersByTime(4000);

    expect(service.toasts().length).toBe(0);
  });

  it('does not remove the toast before the timeout elapses', () => {
    vi.useFakeTimers();

    service.success('Vehicle created.');
    vi.advanceTimersByTime(1000);

    expect(service.toasts().length).toBe(1);
  });

  it('dismiss(id) removes a toast immediately', () => {
    service.success('Vehicle created.');
    const id = service.toasts()[0].id;

    service.dismiss(id);

    expect(service.toasts().length).toBe(0);
  });

  it('dismiss(id) only removes the matching toast', () => {
    service.success('First');
    service.success('Second');
    const firstId = service.toasts()[0].id;

    service.dismiss(firstId);

    expect(service.toasts().length).toBe(1);
    expect(service.toasts()[0].message).toBe('Second');
  });
});
