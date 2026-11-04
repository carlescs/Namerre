import {
  afterNextRender,
  Component,
  type ElementRef,
  effect,
  Injector,
  inject,
  input,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { RenamerFile } from '../../renamer-file';
import { PluginConfigurationComponent } from '../plugin-configuration/plugin-configuration.component';
import { PresetPickerComponent } from '../preset-picker/preset-picker.component';
import { RenameConfirmationComponent } from '../rename-confirmation/rename-confirmation.component';
import { RenamePreviewComponent } from '../rename-preview/rename-preview.component';
import { TmdbMatchingComponent } from '../tmdb-matching/tmdb-matching.component';
import { TmdbPickerComponent } from '../tmdb-picker/tmdb-picker.component';
import { WorkflowStore } from './workflow-store';

/** Renders the rename wizard; its state and transitions live in `WorkflowStore`. */
@Component({
  imports: [
    PluginConfigurationComponent,
    PresetPickerComponent,
    RenameConfirmationComponent,
    RenamePreviewComponent,
    TmdbMatchingComponent,
    TmdbPickerComponent,
  ],
  providers: [WorkflowStore],
  selector: 'app-rename-workflow',
  styleUrl: './rename-workflow.component.scss',
  templateUrl: './rename-workflow.component.html',
})
export class RenameWorkflowComponent {
  readonly files = input.required<RenamerFile[]>();
  /** Related files (subtitles, .nfo, artwork) follow their video instead of running the steps. */
  readonly includeRelated = input(true);
  readonly fileRemoved = output<string>();
  protected readonly store = inject(WorkflowStore);
  private readonly injector = inject(Injector);
  private readonly stageHeading =
    viewChild.required<ElementRef<HTMLHeadingElement>>('stageHeading');

  constructor() {
    effect(() => {
      const files = this.files();
      const includeRelated = this.includeRelated();
      untracked(() => this.store.setCollection(files, includeRelated));
    });
    this.store.stageEntered.pipe(takeUntilDestroyed()).subscribe(() =>
      afterNextRender(() => this.stageHeading().nativeElement.focus(), {
        injector: this.injector,
      }),
    );
  }

  /** Leaves files out of the collection from the preview; the changed collection previews again. */
  protected excludeFiles(paths: readonly string[]): void {
    for (const path of paths) this.fileRemoved.emit(path);
  }
}
