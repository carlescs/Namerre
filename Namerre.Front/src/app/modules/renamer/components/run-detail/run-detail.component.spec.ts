import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RenameEvents } from '../../rename-events';
import { FakeRenameEvents } from '../../rename-events.testing';
import type { RunView } from '../../rename-models';
import { RunDetailComponent } from './run-detail.component';

describe('RunDetailComponent', () => {
  let fixture: ComponentFixture<RunDetailComponent>;
  let http: HttpTestingController;
  const run = (id: string): RunView => ({
    id,
    planId: `plan-${id}`,
    parentRunId: null,
    kind: 'Rename',
    status: 'Completed',
    files: ['a', 'b'].map((file) => ({
      id: file,
      sourcePath: `${id}/${file}.txt`,
      targetPath: `${id}/${file.toUpperCase()}.txt`,
      stagingPath: '',
      hash: `hash-${file}`,
      status: 'Completed',
      error: null,
    })),
    journal: [],
    createdAt: '2026-10-18T00:00:00Z',
    error: null,
  });
  const checkboxes = () =>
    fixture.nativeElement.querySelectorAll('input.file-select') as NodeListOf<HTMLInputElement>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: RenameEvents, useValue: new FakeRenameEvents() },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunDetailComponent);
    fixture.componentRef.setInput('runId', 'run-1');
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
  });

  it('starts over when it is given another run', async () => {
    http.expectOne('/api/rename-runs/run-1').flush(run('run-1'));
    await fixture.whenStable();
    checkboxes()[0].click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('1 file selected');

    fixture.componentRef.setInput('runId', 'run-2');
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Loading history…');
    expect(checkboxes()).toHaveLength(0);
    http.expectOne('/api/rename-runs/run-2').flush(run('run-2'));
    await fixture.whenStable();
    expect(checkboxes()[0].getAttribute('aria-label')).toBe('Select run-2/a.txt to undo');
    expect(fixture.nativeElement.textContent).toContain('0 files selected');
    expect([...checkboxes()].some((box) => box.checked)).toBe(false);
  });

  it('stops listening to the previous run', async () => {
    const first = http.expectOne('/api/rename-runs/run-1');
    fixture.componentRef.setInput('runId', 'run-2');
    await fixture.whenStable();
    expect(first.cancelled).toBe(true);
    http.expectOne('/api/rename-runs/run-2').flush(run('run-2'));
  });
});
