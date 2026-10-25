import { filter, map, Observable, startWith, Subject } from 'rxjs';
import { RenameChange, RenameEvents } from './rename-events';

/** Test double for {@link RenameEvents}: watchers fire once on subscribe and then only on `push`. */
export class FakeRenameEvents implements Pick<RenameEvents, 'watch'> {
  private readonly changes = new Subject<RenameChange>();

  watch(kind: RenameChange['kind'], id?: string): Observable<void> {
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
