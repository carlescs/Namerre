import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import type { PlanView } from '../../rename-models';
import { RunPlanSnapshotComponent } from './run-plan-snapshot.component';

describe('RunPlanSnapshotComponent', () => {
  let fixture: ComponentFixture<RunPlanSnapshotComponent>;
  let http: HttpTestingController;
  const plan = (id: string): PlanView => ({
    id,
    version: 2,
    status: 'Executed',
    completedFiles: 1,
    pipeline: { files: [], plugins: [], selections: [] },
    files: [{ id: 'a', sourcePath: 'a.txt', targetPath: 'A.txt', hash: `hash-${id}`, size: 1 }],
    issues: [],
    createdAt: '2026-10-18T00:00:00Z',
  });
  const open = async () => {
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    await fixture.whenStable();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunPlanSnapshotComponent);
    fixture.componentRef.setInput('planId', 'plan-1');
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  it('reads nothing until it is opened', () => {
    http.expectNone('/api/rename-plans/plan-1');
  });

  it('forgets the loaded plan when the run shows another one', async () => {
    await open();
    http.expectOne('/api/rename-plans/plan-1').flush(plan('plan-1'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('hash-plan-1');
    fixture.componentRef.setInput('planId', 'plan-2');
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('hash-plan-1');
    await open();
    http.expectOne('/api/rename-plans/plan-2').flush(plan('plan-2'));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('hash-plan-2');
  });

  it('ignores an answer for a plan that is no longer shown', async () => {
    await open();
    const stale = http.expectOne('/api/rename-plans/plan-1');
    fixture.componentRef.setInput('planId', 'plan-2');
    await fixture.whenStable();
    expect(stale.cancelled).toBe(true);
    expect(fixture.nativeElement.textContent).not.toContain('Loading saved settings');
  });
});
