import { type ComponentFixture, TestBed } from '@angular/core/testing';
import type { PluginInstance } from '../../rename-api';
import { buildPipeline, presetDefaults, simplePresets } from '../../simple-presets';
import { PresetPickerComponent } from './preset-picker.component';

describe('PresetPickerComponent', () => {
  let fixture: ComponentFixture<PresetPickerComponent>;
  let emitted: PluginInstance[][];
  const [tv, movies, , numbers] = simplePresets;

  beforeEach(async () => {
    fixture = TestBed.createComponent(PresetPickerComponent);
    emitted = [];
    fixture.componentInstance.pluginsChanged.subscribe((plugins) => emitted.push(plugins));
    await fixture.whenStable();
  });

  function card(name: string): HTMLButtonElement {
    const found = [...fixture.nativeElement.querySelectorAll('.card')].find((item) =>
      (item as HTMLElement).textContent?.includes(name),
    );
    if (!found) throw new Error(`No card ${name}`);
    return found as HTMLButtonElement;
  }

  function field(label: string): HTMLInputElement | HTMLSelectElement {
    const labelElement = [...fixture.nativeElement.querySelectorAll('label')].find((item) =>
      (item as HTMLElement).textContent?.trim().startsWith(label),
    ) as HTMLLabelElement | undefined;
    const control = labelElement?.querySelector('input, select');
    if (!control) throw new Error(`No field ${label}`);
    return control as HTMLInputElement | HTMLSelectElement;
  }

  async function show(plugins: PluginInstance[]): Promise<void> {
    fixture.componentRef.setInput('plugins', plugins);
    await fixture.whenStable();
  }

  it('lists the built-in presets as cards and builds the chosen one', async () => {
    const cards = [...fixture.nativeElement.querySelectorAll('.card')] as HTMLButtonElement[];
    expect(cards.map((item) => item.querySelector('.card-title')?.textContent)).toEqual([
      'TV episodes (TMDB)',
      'Movies (TMDB)',
      'Clean up names',
      'Number files',
    ]);
    expect(cards.every((item) => item.getAttribute('aria-pressed') === 'false')).toBe(true);
    expect(fixture.nativeElement.querySelector('fieldset')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-plugin-editor')).toBeNull();

    card('Movies (TMDB)').click();
    expect(emitted).toHaveLength(1);
    expect(emitted[0].map((step) => [step.typeId, step.options.nameTemplate])).toEqual([
      ['tmdb', '{title} ({year})'],
    ]);
  });

  it('marks the preset that built the steps and shows only its key fields', async () => {
    await show(buildPipeline(tv, presetDefaults(tv)));
    expect(card('TV episodes').getAttribute('aria-pressed')).toBe('true');
    expect(fixture.nativeElement.querySelector('legend').textContent).toContain(
      'TV episodes (TMDB) options',
    );
    expect(field('Metadata language').value).toBe('en-US');
    expect(field('Name style').value).toBe('{title} - S{season}E{episode} - {episodeTitle}');
    expect(fixture.nativeElement.querySelector('datalist option[value="es-ES"]')).not.toBeNull();
    card('TV episodes').click();
    expect(emitted).toEqual([]);
  });

  it('changes an option of the chosen preset and keeps its step ids', async () => {
    const steps = buildPipeline(tv, presetDefaults(tv));
    await show(steps);
    const language = field('Metadata language');
    language.value = 'ca-ES';
    language.dispatchEvent(new Event('change'));
    expect(emitted[0][0]).toEqual({
      ...steps[0],
      options: { ...steps[0].options, language: 'ca-ES' },
    });
    expect(emitted[0].map((step) => step.instanceId)).toEqual(steps.map((step) => step.instanceId));
    language.value = '  ';
    language.dispatchEvent(new Event('change'));
    expect(language.value).toBe('en-US');
    expect(emitted).toHaveLength(1);
  });

  it('carries the metadata language over to another preset', async () => {
    await show(buildPipeline(tv, { ...presetDefaults(tv), language: 'es-ES' }));
    card('Movies (TMDB)').click();
    expect(emitted[0][0].options.language).toBe('es-ES');
    expect(emitted[0][0].options.nameTemplate).toBe(movies.steps[0].options.nameTemplate);
  });

  it('keeps numbers whole and in range', async () => {
    await show(buildPipeline(numbers, presetDefaults(numbers)));
    const width = field('Digits') as HTMLInputElement;
    width.value = '99';
    width.dispatchEvent(new Event('change'));
    expect(width.value).toBe('10');
    expect(emitted[0][0].options['width']).toBe(10);
    const start = field('Start at') as HTMLInputElement;
    start.value = '';
    start.dispatchEvent(new Event('change'));
    expect(start.value).toBe('1');
    expect(emitted).toHaveLength(1);
  });

  it('explains when Advanced steps match no preset', async () => {
    const [step] = buildPipeline(numbers, presetDefaults(numbers));
    await show([{ ...step, enabled: false }]);
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain(
      'no preset matches them',
    );
    expect(fixture.nativeElement.querySelector('[aria-pressed="true"]')).toBeNull();
    card('Number files').click();
    expect(emitted[0][0].enabled).toBe(true);
  });

  it('disables the cards and fields while a plan is prepared', async () => {
    await show(buildPipeline(numbers, presetDefaults(numbers)));
    fixture.componentRef.setInput('disabled', true);
    await fixture.whenStable();
    expect(card('Clean up names').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('fieldset').disabled).toBe(true);
  });
});
