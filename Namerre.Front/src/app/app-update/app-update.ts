import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';

export type UpdateState = 'none' | 'ready' | 'broken';

/**
 * Tells the user when the service worker has downloaded a newer Namerre build, so an installed app
 * does not keep running a stale UI against an upgraded server. Reloading is left to the user; rename
 * jobs run on the server and are unaffected by it.
 */
@Component({
  selector: 'app-update',
  styleUrl: './app-update.scss',
  templateUrl: './app-update.html',
})
export class AppUpdate {
  private readonly document = inject(DOCUMENT);
  protected readonly state = signal<UpdateState>('none');

  constructor() {
    const updates = inject(SwUpdate);
    if (!updates.isEnabled) return;
    const destroyRef = inject(DestroyRef);
    const versions = updates.versionUpdates.subscribe((event) => {
      if (event.type === 'VERSION_READY') this.state.set('ready');
    });
    const unrecoverable = updates.unrecoverable.subscribe(() => this.state.set('broken'));
    // Installed apps stay open for days, so look for a new build whenever the window is shown again.
    const check = () => {
      if (this.document.visibilityState === 'visible') updates.checkForUpdate().catch(() => {});
    };
    this.document.addEventListener('visibilitychange', check);
    destroyRef.onDestroy(() => {
      versions.unsubscribe();
      unrecoverable.unsubscribe();
      this.document.removeEventListener('visibilitychange', check);
    });
  }

  protected reload(): void {
    this.document.location.reload();
  }

  protected dismiss(): void {
    this.state.set('none');
  }
}
