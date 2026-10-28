import { filter, isObservable, map, type Observable, Subject, startWith } from 'rxjs';
import type { RenameChange, RenameEvents } from './rename-events';

/**
 * Test double for {@link RenameEvents}: watchers fire once on subscribe and then only on `push`.
 * `polling` reflects the latest fallback-poll request of the most recent watcher.
 */
export class FakeRenameEvents implements Pick<RenameEvents, 'watch'> {
  private readonly changes = new Subject<RenameChange>();
  polling = false;

  watch(
    kind: RenameChange['kind'],
    id?: string,
    poll: boolean | Observable<boolean> = true,
  ): Observable<void> {
    if (isObservable(poll)) poll.subscribe((value) => (this.polling = value));
    else this.polling = poll;
    return this.changes.pipe(
      filter((change) => change.kind === kind && (!id || change.id === id)),
      map(() => undefined),
      startWith(undefined),
    );
  }

  push(kind: RenameChange['kind'], id: string, status = 'Changed'): void {
    this.changes.next({ kind, id, status });
  }
}
