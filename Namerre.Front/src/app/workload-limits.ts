import { Injectable, signal } from '@angular/core';

/** Workload limits the server enforces (`Namerre:Limits`), reported by the session endpoint. */
export interface Limits {
  maxFilesPerRun: number;
  maxStepsPerRun: number;
  maxQueuedJobs: number;
}

/** The server's defaults, used until a signed-in session reports the configured values. */
export const defaultLimits: Limits = { maxFilesPerRun: 200, maxStepsPerRun: 32, maxQueuedJobs: 32 };

@Injectable({ providedIn: 'root' })
export class WorkloadLimits {
  readonly current = signal<Limits>(defaultLimits);
}
