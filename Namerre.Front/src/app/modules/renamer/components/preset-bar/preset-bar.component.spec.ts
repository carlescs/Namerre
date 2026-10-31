import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import type { PluginInstance, PresetView } from '../../rename-api';
import { PresetBarComponent } from './preset-bar.component';

describe('PresetBarComponent', () => {
  let fixture: ComponentFixture<PresetBarComponent>;
  let http: HttpTestingController;
  const step: PluginInstance = {
    instanceId: 'step-1',
    typeId: 'case',
    version: 1,
    enabled: true,
    options: { mode: 'upper' },
  };
  const preset: PresetView = {
    id: 'p1',
    name: 'Shows',
    plugins: [step],
    verifyContent: true,
    deleteEmptyFolders: false,
    onCollision: 'AddNumber',
    createdAt: '2026-10-31T00:00:00Z',
    updatedAt: '2026-10-31T00:00:00Z',
    unknownSteps: [],
  };
  const summary = { id: 'p1', name: 'Shows', steps: 1, updatedAt: preset.updatedAt };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PresetBarComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PresetBarComponent);
    fixture.componentRef.setInput('plugins', [step]);
    fixture.componentRef.setInput('options', {
      verifyContent: true,
      deleteEmptyFolders: false,
      numberCollisions: true,
    });
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
  });

  it('lists presets and loads the chosen one', async () => {
    http.expectOne('/api/rename-presets').flush([summary]);
    await fixture.whenStable();
    expect(text()).toContain('Shows (1 step)');
    const loaded = vi.fn();
    fixture.componentInstance.loaded.subscribe(loaded);
    expect(button('Load').disabled).toBe(true);
    choose('p1');
    await fixture.whenStable();
    button('Load').click();
    http.expectOne('/api/rename-presets/p1').flush(preset);
    await fixture.whenStable();
    expect(loaded).toHaveBeenCalledWith(preset);
    expect(text()).toContain('Loaded Shows.');
    expect(button('Save changes to Shows')).toBeDefined();
  });

  it('warns when a loaded preset has steps the server no longer knows', async () => {
    http.expectOne('/api/rename-presets').flush([summary]);
    await fixture.whenStable();
    choose('p1');
    await fixture.whenStable();
    button('Load').click();
    http.expectOne('/api/rename-presets/p1').flush({ ...preset, unknownSteps: ['step-1'] });
    await fixture.whenStable();
    expect(text()).toContain('Some steps need attention');
  });

  it('saves the current steps and run options as a new preset', async () => {
    http.expectOne('/api/rename-presets').flush([]);
    await fixture.whenStable();
    expect(text()).toContain('No presets yet');
    expect(button('Save as new preset').disabled).toBe(true);
    type(' Shows ');
    await fixture.whenStable();
    button('Save as new preset').click();
    const create = http.expectOne('/api/rename-presets');
    expect(create.request.method).toBe('POST');
    expect(create.request.body).toEqual({
      name: 'Shows',
      plugins: [step],
      verifyContent: true,
      deleteEmptyFolders: false,
      onCollision: 'AddNumber',
    });
    create.flush(preset);
    http.expectOne('/api/rename-presets').flush([summary]);
    await fixture.whenStable();
    expect(text()).toContain('Saved Shows.');
  });

  it('saves changes to, renames and deletes the active preset', async () => {
    http.expectOne('/api/rename-presets').flush([summary]);
    await fixture.whenStable();
    choose('p1');
    await fixture.whenStable();
    button('Load').click();
    http.expectOne('/api/rename-presets/p1').flush(preset);
    await fixture.whenStable();
    button('Save changes to Shows').click();
    const update = http.expectOne('/api/rename-presets/p1');
    expect(update.request.method).toBe('PUT');
    update.flush(preset);
    http.expectOne('/api/rename-presets').flush([summary]);
    await fixture.whenStable();
    type('Series');
    await fixture.whenStable();
    button('Rename only').click();
    const rename = http.expectOne('/api/rename-presets/p1');
    expect(rename.request.method).toBe('PATCH');
    expect(rename.request.body).toEqual({ name: 'Series' });
    rename.flush({ ...preset, name: 'Series' });
    http.expectOne('/api/rename-presets').flush([{ ...summary, name: 'Series' }]);
    await fixture.whenStable();
    button('Delete').click();
    await fixture.whenStable();
    http.expectNone('/api/rename-presets/p1');
    button('Confirm delete').click();
    const remove = http.expectOne('/api/rename-presets/p1');
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null);
    http.expectOne('/api/rename-presets').flush([]);
    await fixture.whenStable();
    expect(text()).toContain('Deleted Series.');
    expect(text()).not.toContain('Save changes');
  });

  it('reports a taken name', async () => {
    http.expectOne('/api/rename-presets').flush([summary]);
    type('shows');
    await fixture.whenStable();
    button('Save as new preset').click();
    http
      .expectOne('/api/rename-presets')
      .flush(
        { title: 'Another preset already has this name.', code: 'PresetNameTaken' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    expect(text()).toContain('Another preset already has this name.');
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function button(prefix: string): HTMLButtonElement {
    const result = Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((candidate) => candidate.textContent?.trim().startsWith(prefix));
    if (!result) throw new Error(`Button not found: ${prefix}`);
    return result;
  }

  function choose(id: string): void {
    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = id;
    select.dispatchEvent(new Event('change'));
  }

  function type(name: string): void {
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = name;
    input.dispatchEvent(new Event('input'));
  }
});
