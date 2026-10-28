import { Injectable, InjectionToken, inject } from '@angular/core';
import { type HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import {
  BehaviorSubject,
  combineLatest,
  distinctUntilChanged,
  EMPTY,
  filter,
  map,
  merge,
  Observable,
  of,
  share,
  switchMap,
  timer,
} from 'rxjs';

/** A server signal that a durable plan or run changed; the record itself is always re-read over REST. */
export interface RenameChange {
  kind: 'Plan' | 'Run';
  id: string;
  status: string;
}

export const renameHubPath = '/api/hubs/rename';
/** Safety-net refresh while pushed changes arrive, in case one was missed. */
export const connectedRefreshMs = 15000;
/** Refresh cadence whenever the hub is unreachable (blocked WebSockets, restarting server, ...). */
export const disconnectedRefreshMs = 1000;

export const RENAME_HUB = new InjectionToken<() => HubConnection>('RENAME_HUB', {
  providedIn: 'root',
  factory: () => () =>
    new HubConnectionBuilder()
      .withUrl(renameHubPath)
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retry) => retryDelay(retry.previousRetryCount),
      })
      .configureLogging(LogLevel.None)
      .build(),
});

function retryDelay(attempt: number): number {
  return Math.min(30000, 1000 * 2 ** attempt);
}

@Injectable({ providedIn: 'root' })
export class RenameEvents {
  private readonly createConnection = inject(RENAME_HUB);
  private readonly connected = new BehaviorSubject(false);
  // One shared connection while anything watches; a short grace period avoids reconnecting on navigation.
  private readonly changes: Observable<RenameChange> = new Observable<RenameChange>(
    (subscriber) => {
      const connection = this.createConnection();
      let stopped = false;
      let attempts = 0;
      let retry: ReturnType<typeof setTimeout> | undefined;
      const start = () => {
        connection.start().then(
          () => {
            attempts = 0;
            if (!stopped) this.connected.next(true);
          },
          () => {
            // Automatic reconnect only covers dropped connections; a failed start retries here.
            if (!stopped) retry = setTimeout(start, retryDelay(attempts++));
          },
        );
      };
      connection.on('renameChanged', (change: RenameChange) => subscriber.next(change));
      connection.onreconnecting(() => this.connected.next(false));
      connection.onreconnected(() => this.connected.next(true));
      connection.onclose(() => {
        this.connected.next(false);
        if (!stopped) retry = setTimeout(start, retryDelay(attempts++));
      });
      start();
      return () => {
        stopped = true;
        clearTimeout(retry);
        this.connected.next(false);
        void connection.stop();
      };
    },
  ).pipe(share({ resetOnRefCountZero: () => timer(2000) }));

  /**
   * Emits whenever a record of `kind` (optionally only `id`) should be re-read: immediately, on each
   * pushed change and after every connection change. While `poll` is (or last emitted) true, it also
   * refreshes on a fallback timer that is fast while disconnected, for views that must not stall when
   * the hub is unreachable; pushed changes keep arriving either way.
   */
  watch(
    kind: RenameChange['kind'],
    id?: string,
    poll: boolean | Observable<boolean> = true,
  ): Observable<void> {
    return merge(
      this.changes.pipe(filter((change) => change.kind === kind && (!id || change.id === id))),
      // Initial load and resync after every connection change; quick flaps collapse into one.
      this.connected.pipe(
        distinctUntilChanged(),
        switchMap(() => timer(0)),
      ),
      combineLatest([
        this.connected.pipe(distinctUntilChanged()),
        (typeof poll === 'boolean' ? of(poll) : poll).pipe(distinctUntilChanged()),
      ]).pipe(
        switchMap(([connected, polling]) => {
          const interval = connected ? connectedRefreshMs : disconnectedRefreshMs;
          return polling ? timer(interval, interval) : EMPTY;
        }),
      ),
    ).pipe(map(() => undefined));
  }
}

/**
 * Loads on every trigger without overlapping requests: triggers that arrive while a load is in flight
 * (or within `spacing` ms after it) collapse into one follow-up load, so the final state is never missed.
 */
export function refreshOn<T>(
  triggers: Observable<unknown>,
  load: () => Observable<T>,
  spacing = 250,
): Observable<T> {
  return new Observable<T>((subscriber) => {
    let busy = false;
    let pending = false;
    let cooldown: ReturnType<typeof setTimeout> | undefined;
    let current: { unsubscribe(): void } | undefined;
    const run = () => {
      busy = true;
      pending = false;
      current = load().subscribe({
        next: (value) => subscriber.next(value),
        error: (error: unknown) => subscriber.error(error),
        complete: () => {
          cooldown = setTimeout(() => {
            busy = false;
            if (pending) run();
          }, spacing);
        },
      });
    };
    const source = triggers.subscribe({
      next: () => {
        if (busy) pending = true;
        else run();
      },
      error: (error: unknown) => subscriber.error(error),
    });
    return () => {
      source.unsubscribe();
      current?.unsubscribe();
      clearTimeout(cooldown);
    };
  });
}
