import { Component, computed, input } from '@angular/core';
import { describeChange } from '../../path-diff';

interface Segment {
  text: string;
  changed: boolean;
}

/** One table cell's worth of a rename: the old or the new name, with what changed highlighted. */
@Component({
  selector: 'app-name-change',
  templateUrl: './name-change.component.html',
  styleUrl: './name-change.component.scss',
})
export class NameChangeComponent {
  readonly sourcePath = input.required<string>();
  readonly targetPath = input.required<string>();
  readonly side = input<'from' | 'to'>('from');
  protected readonly change = computed(() => describeChange(this.sourcePath(), this.targetPath()));
  protected readonly segments = computed<Segment[]>(() => {
    const { diff } = this.change();
    const middle = this.side() === 'from' ? diff.removed : diff.added;
    return [
      { text: diff.prefix, changed: false },
      { text: middle, changed: true },
      { text: diff.suffix, changed: false },
    ].filter((segment) => segment.text);
  });
}
