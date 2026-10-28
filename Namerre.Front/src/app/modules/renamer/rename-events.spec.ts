import { TestBed } from '@angular/core/testing';
import type { HubConnection } from '@microsoft/signalr';
import { BehaviorSubject, type Observable, Subject } from 'rxjs';
import { RENAME_HUB, type RenameChange, RenameEvents, refreshOn } from './rename-events';

class FakeHub {
  readonly handlers = new Map<string, (change: RenameChange) => void>();
  reconnecting?: () => void;
  reconnected?: () => void;
  closed?: () => void;
  starts = 0;
  stops = 0;
  failStarts = 0;

  on(method: string, handler: (change: RenameChange) => void): void {
    this.handlers.set(method, handler);
  }
  onreconnecting(callback: () => void): void {
    this.reconnecting = callback;
  }
  onreconnected(callback: () => void): void {
    this.reconnected = callback;
  }
  onclose(callback: () => void): void {
    this.closed = callback;
  }
  start(): Promise<void> {
    this.starts++;
    return this.failStarts-- > 0 ? Promise.reject(new Error('offline')) : Promise.resolve();
  }
  stop(): Promise<void> {
    this.stops++;
    return Promise.resolve();
  }
  push(change: RenameChange): void {
    this.handlers.get('renameChanged')!(change);
  }
}

describe('RenameEvents', () => {
  let hub: FakeHub;
  let created: number;

  beforeEach(() => {
    vi.useFakeTimers();
    hub = new FakeHub();
    created = 0;
    TestBed.configureTestingModule({
      providers: [
        {
          provide: RENAME_HUB,
          useValue: () => {
            created++;
            return hub as unknown as HubConnection;
          },
        },
      ],
    });
  });

  afterEach(() => vi.useRealTimers());

  function record(source: Observable<void>): { count: number; stop(): void } {
    const result = { count: 0, stop: () => subscription.unsubscribe() };
    const subscription = source.subscribe(() => result.count++);
    return result;
  }

  it('fires once on connect and then on matching pushes only', async () => {
    const events = TestBed.inject(RenameEvents);
    const watcher = record(events.watch('Run', 'run-1'));
    await vi.advanceTimersByTimeAsync(1);
    // The initial refresh and the post-connect resync collapse when the hub connects immediately.
    expect(watcher.count).toBe(1);

    hub.push({ kind: 'Run', id: 'run-2', status: 'Running' });
    hub.push({ kind: 'Plan', id: 'run-1', status: 'Ready' });
    expect(watcher.count).toBe(1);
    hub.push({ kind: 'Run', id: 'run-1', status: 'Running' });
    expect(watcher.count).toBe(2);

    hub.reconnecting!();
    hub.reconnected!();
    await vi.advanceTimersByTimeAsync(1);
    // Changes may have been missed while the connection was down.
    expect(watcher.count).toBe(3);
    watcher.stop();
  });

  it('polls quickly while disconnected and only as a safety net while connected', async () => {
    hub.failStarts = 1;
    const events = TestBed.inject(RenameEvents);
    const watcher = record(events.watch('Plan', 'plan-1'));
    await vi.advanceTimersByTimeAsync(0);
    expect(watcher.count).toBe(1);
    await vi.advanceTimersByTimeAsync(900);
    expect(watcher.count).toBe(1);
    // The failed start retries after one second; the disconnected poll fires on the same beat.
    await vi.advanceTimersByTimeAsync(101);
    expect(hub.starts).toBe(2);
    const connected = watcher.count;
    await vi.advanceTimersByTimeAsync(14000);
    expect(watcher.count).toBe(connected);
    await vi.advanceTimersByTimeAsync(1000);
    expect(watcher.count).toBe(connected + 1);

    hub.reconnecting!();
    await vi.advanceTimersByTimeAsync(0);
    const reconnecting = watcher.count;
    await vi.advanceTimersByTimeAsync(3000);
    expect(watcher.count).toBe(reconnecting + 3);
    watcher.stop();
  });

  it('does not poll views that opt out', async () => {
    const events = TestBed.inject(RenameEvents);
    const watcher = record(events.watch('Run', undefined, false));
    await vi.advanceTimersByTimeAsync(60000);
    expect(watcher.count).toBe(1);
    hub.push({ kind: 'Run', id: 'any', status: 'Deleted' });
    expect(watcher.count).toBe(2);
    watcher.stop();
  });

  it('starts and stops the fallback poll as the view asks, without an extra refresh', async () => {
    const events = TestBed.inject(RenameEvents);
    const poll = new BehaviorSubject(true);
    const watcher = record(events.watch('Run', 'run-1', poll));
    await vi.advanceTimersByTimeAsync(1);
    expect(watcher.count).toBe(1);
    await vi.advanceTimersByTimeAsync(15000);
    expect(watcher.count).toBe(2);

    poll.next(false);
    await vi.advanceTimersByTimeAsync(60000);
    expect(watcher.count).toBe(2);
    hub.push({ kind: 'Run', id: 'run-1', status: 'Completed' });
    expect(watcher.count).toBe(3);

    poll.next(true);
    await vi.advanceTimersByTimeAsync(15000);
    expect(watcher.count).toBe(4);
    watcher.stop();
  });

  it('shares one connection and stops it shortly after the last watcher leaves', async () => {
    const events = TestBed.inject(RenameEvents);
    const first = record(events.watch('Run', 'run-1'));
    const second = record(events.watch('Plan', 'plan-1'));
    await vi.advanceTimersByTimeAsync(0);
    expect(created).toBe(1);
    expect(hub.starts).toBe(1);

    first.stop();
    second.stop();
    await vi.advanceTimersByTimeAsync(1000);
    const third = record(events.watch('Run', 'run-1'));
    expect(created).toBe(1);
    third.stop();
    await vi.advanceTimersByTimeAsync(2000);
    expect(hub.stops).toBe(1);
    hub.closed!();
    await vi.advanceTimersByTimeAsync(60000);
    expect(hub.starts).toBe(1);
  });
});

describe('refreshOn', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('never overlaps loads and collapses triggers during a load into one follow-up', async () => {
    const triggers = new Subject<void>();
    const loads: Subject<string>[] = [];
    const values: string[] = [];
    const subscription = refreshOn(triggers, () => {
      const load = new Subject<string>();
      loads.push(load);
      return load;
    }).subscribe((value) => values.push(value));

    triggers.next();
    triggers.next();
    triggers.next();
    expect(loads.length).toBe(1);
    loads[0].next('first');
    loads[0].complete();
    await vi.advanceTimersByTimeAsync(249);
    expect(loads.length).toBe(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(loads.length).toBe(2);
    loads[1].next('second');
    loads[1].complete();
    await vi.advanceTimersByTimeAsync(1000);
    expect(loads.length).toBe(2);

    triggers.next();
    expect(loads.length).toBe(3);
    expect(values).toEqual(['first', 'second']);
    subscription.unsubscribe();
  });

  it('propagates load errors and cancels the in-flight load on unsubscribe', () => {
    const triggers = new Subject<void>();
    const failing = new Subject<string>();
    let error: unknown;
    refreshOn(triggers, () => failing).subscribe({ error: (e: unknown) => (error = e) });
    triggers.next();
    failing.error('boom');
    expect(error).toBe('boom');

    const pending = new Subject<string>();
    const subscription = refreshOn(triggers, () => pending).subscribe();
    triggers.next();
    expect(pending.observed).toBe(true);
    subscription.unsubscribe();
    expect(pending.observed).toBe(false);
  });
});
