import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { WorkflowMode, workflowModeStorageKey } from '../../../../workflow-mode/workflow-mode';
import { counterPlugin, tmdbPlugin } from '../../plugin-catalog.testing';
import type { PlanView, PreviewResult } from '../../rename-api';
import { RenameEvents } from '../../rename-events';
import { FakeRenameEvents } from '../../rename-events.testing';
import type { RenamerFile } from '../../renamer-file';
import { RenameWorkflowComponent } from './rename-workflow.component';

describe('RenameWorkflowComponent in Simple mode', () => {
  let fixture: ComponentFixture<RenameWorkflowComponent>;
  let http: HttpTestingController;
  const files: RenamerFile[] = [
    { name: 'a.txt', path: 'a.txt', folderPath: '', length: 1, lastModifiedUtc: '2026-10-31' },
  ];
  const preview: PreviewResult = {
    files: [
      {
        id: 'a.txt',
        sourcePath: 'a.txt',
        targetPath: '01 - a.txt',
        unchanged: false,
        steps: [],
        issues: [],
      },
    ],
    canPrepare: true,
  };
  const ready: PlanView = {
    id: 'plan-1',
    version: 1,
    status: 'Ready',
    completedFiles: 1,
    pipeline: { files: [{ id: 'a.txt', path: 'a.txt' }], plugins: [], selections: [] },
    files: [{ id: 'a.txt', sourcePath: 'a.txt', targetPath: '01 - a.txt', hash: 'h', size: 1 }],
    issues: [],
    createdAt: '2026-10-31T00:00:00Z',
  };

  beforeEach(async () => {
    localStorage.clear();
    localStorage.setItem(workflowModeStorageKey, 'simple');
    await TestBed.configureTestingModule({
      imports: [RenameWorkflowComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: RenameEvents, useValue: new FakeRenameEvents() },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RenameWorkflowComponent);
    fixture.componentRef.setInput('files', files);
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
    localStorage.clear();
  });

  it('goes from files to a preset to one review and rename screen with safe defaults', async () => {
    expect(stageTitles()).toEqual(['Select files', 'Choose a preset', 'Review and rename']);
    await openPresets();
    expect(heading().textContent).toBe('Choose a preset');
    expect(fixture.nativeElement.querySelector('app-plugin-editor')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-preset-bar')).toBeNull();
    expect(button('Next: review and rename').disabled).toBe(true);

    await chooseNumbers();
    expect(heading().textContent).toBe('Review and rename');
    expect(fixture.nativeElement.querySelector('#number-collisions')).toBeNull();
    expect(fixture.nativeElement.querySelector('#quick-check')).toBeNull();
    expect(fixture.nativeElement.querySelector('#delete-empty-folders')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-rename-preview').hidden).toBe(false);

    button('Check files and continue').click();
    http.expectOne('/api/rename-plans').flush({ id: 'plan-1', status: 'Preparing', location: '' });
    await new Promise((resolve) => setTimeout(resolve, 10));
    http.expectOne('/api/rename-plans/plan-1').flush(ready);
    await fixture.whenStable();
    expect(heading().textContent).toBe('Review and rename');
    expect(fixture.nativeElement.querySelector('app-rename-preview').hidden).toBe(true);
    expect(fixture.nativeElement.querySelector('app-rename-confirmation').hidden).toBe(false);
    expect(button('Rename 1 file').disabled).toBe(false);
    expect(() => button('Check files and continue')).toThrow();

    button('Back').click();
    await fixture.whenStable();
    expect(heading().textContent).toBe('Choose a preset');
  });

  it('adds the matching stage for a TMDB preset', async () => {
    await openPresets([counterPlugin, tmdbPlugin]);
    button('Movies (TMDB)').click();
    await fixture.whenStable();
    expect(stageTitles()).toEqual([
      'Select files',
      'Choose a preset',
      'Match TMDB metadata',
      'Review and rename',
    ]);
    button('Next: match metadata').click();
    const request = http.expectOne('/api/rename-previews');
    expect(request.request.body.plugins[0].options.nameTemplate).toBe('{title} ({year})');
    request.flush({ ...preview, files: [{ ...preview.files[0], unchanged: true }] });
    await fixture.whenStable();
    expect(heading().textContent).toBe('Match TMDB metadata');
  });

  it('opens the preset in Advanced with every option and the stage intact', async () => {
    await openPresets();
    await chooseNumbers();
    const built = draftPlugins();

    await TestBed.inject(WorkflowMode).request('advanced');
    await fixture.whenStable();
    http.expectOne('/api/rename-presets').flush([]);
    await fixture.whenStable();
    expect(heading().textContent).toBe('Review filenames');
    expect(fixture.nativeElement.querySelector('#number-collisions')).not.toBeNull();
    expect(text()).toContain('01 - a.txt');
    http.expectNone('/api/rename-previews');

    button('Back').click();
    await fixture.whenStable();
    expect(heading().textContent).toBe('Configure plugins');
    const position = fixture.nativeElement.querySelector(
      'app-plugin-editor select',
    ) as HTMLSelectElement;
    expect(position.value).toBe('prefix');
    expect(draftPlugins()).toEqual(built);

    await TestBed.inject(WorkflowMode).request('simple');
    await fixture.whenStable();
    expect(heading().textContent).toBe('Choose a preset');
    expect(button('Number files').getAttribute('aria-pressed')).toBe('true');
    expect(draftPlugins()).toEqual(built);
  });

  it('starts afresh when the user discards Advanced steps', async () => {
    await openPresets();
    await chooseNumbers();
    const mode = TestBed.inject(WorkflowMode);
    await mode.request('advanced');
    await fixture.whenStable();
    http.expectOne('/api/rename-presets').flush([]);
    button('Back').click();
    await fixture.whenStable();
    const toggle = Array.from(
      fixture.nativeElement.querySelectorAll('app-plugin-editor label') as NodeListOf<HTMLElement>,
    )
      .find((label) => label.textContent?.includes('Restart the count'))
      ?.querySelector('input') as HTMLInputElement;
    toggle.click();
    await fixture.whenStable();
    expect(draftPlugins()[0].options['perFolder']).toBe(true);

    await mode.request('simple');
    expect(mode.pending()).toBe('simple');
    mode.confirm();
    await fixture.whenStable();
    expect(heading().textContent).toBe('Choose a preset');
    expect(draftPlugins()).toEqual([]);
    expect(fixture.nativeElement.querySelector('[aria-pressed="true"]')).toBeNull();
    expect(button('Next: review and rename').disabled).toBe(true);
    for (const request of http.match('/api/rename-previews')) request.flush(preview);
  });

  async function openPresets(catalog = [counterPlugin]): Promise<void> {
    button('Next: choose a preset').click();
    http.expectOne('/api/rename-plugins').flush(catalog);
    await fixture.whenStable();
  }

  async function chooseNumbers(): Promise<void> {
    button('Number files').click();
    await fixture.whenStable();
    expect(button('Number files').getAttribute('aria-pressed')).toBe('true');
    button('Next: review and rename').click();
    const request = http.expectOne('/api/rename-previews');
    expect(request.request.body).toMatchObject({
      verifyContent: false,
      deleteEmptyFolders: true,
      includeCompanions: true,
      onCollision: 'Report',
    });
    expect(request.request.body.plugins).toHaveLength(1);
    expect(request.request.body.plugins[0]).toMatchObject({
      typeId: 'counter',
      enabled: true,
      options: { position: 'prefix', start: 1, width: 2 },
    });
    request.flush(preview);
    await fixture.whenStable();
  }

  function draftPlugins(): { options: Record<string, unknown> }[] {
    return JSON.parse(localStorage.getItem('namerre.draft.pipeline.v1') ?? '{"plugins":[]}')
      .plugins;
  }

  function stageTitles(): string[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll('.step .text-xs') as NodeListOf<HTMLElement>,
    ).map((item) => item.textContent?.trim() ?? '');
  }

  function text(): string {
    return fixture.nativeElement.textContent;
  }

  function heading(): HTMLHeadingElement {
    return fixture.nativeElement.querySelector('.stage-header h2');
  }

  function button(prefix: string): HTMLButtonElement {
    const result = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((candidate) => candidate.textContent?.trim().startsWith(prefix));
    if (!result) throw new Error(`Button not found: ${prefix}`);
    return result;
  }
});
