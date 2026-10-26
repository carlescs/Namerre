import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PluginDescriptor, PluginInstance } from '../../rename-api';
import { PluginEditorComponent } from './plugin-editor.component';

const testCatalog: PluginDescriptor[] = [
  {
    typeId: 'literal',
    version: 1,
    name: 'Literal replacement',
    defaultOptions: { search: '', replacement: '', ignoreCase: false, scope: 'all' },
  },
  { typeId: 'case', version: 1, name: 'Case change', defaultOptions: { mode: 'upper' } },
  {
    typeId: 'tmdb',
    version: 1,
    name: 'TMDB',
    defaultOptions: {
      nameTemplate: '{title} ({year})',
      changeFolder: false,
      baseFolder: '',
      folderTemplate: '{title} ({year})',
      seasonFolder: false,
      seasonFolderTemplate: 'Season {season}',
      language: 'en-US',
    },
  },
];

describe('PluginEditorComponent', () => {
  let fixture: ComponentFixture<PluginEditorComponent>;
  let plugins: PluginInstance[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [PluginEditorComponent] }).compileComponents();
    fixture = TestBed.createComponent(PluginEditorComponent);
    fixture.componentRef.setInput('catalog', testCatalog);
    plugins = [];
    fixture.componentInstance.pluginsChanged.subscribe((value) => (plugins = value));
    await fixture.whenStable();
  });

  it('restores saved plugins without focusing them and drops unknown types', async () => {
    const saved: PluginInstance = {
      instanceId: 'saved-case',
      typeId: 'case',
      version: 1,
      enabled: false,
      options: { mode: 'lower' },
    };
    const restored = TestBed.createComponent(PluginEditorComponent);
    restored.componentRef.setInput('catalog', testCatalog);
    restored.componentRef.setInput('initialPlugins', [
      saved,
      { ...saved, instanceId: 'gone', typeId: 'removed-type' },
    ]);
    let emitted: PluginInstance[] | undefined;
    restored.componentInstance.pluginsChanged.subscribe((value) => (emitted = value));
    await restored.whenStable();
    const element: HTMLElement = restored.nativeElement;
    expect(element.querySelectorAll('article')).toHaveLength(1);
    expect((element.querySelector('article select') as HTMLSelectElement).value).toBe('lower');
    expect(emitted).toEqual([saved]);
    expect(element.contains(document.activeElement)).toBe(false);
    restored.destroy();
  });

  it('starts empty and allows repeated types with independently configured options', async () => {
    expect(fixture.nativeElement.textContent).toContain('No plugins selected');
    expect(plugins).toEqual([]);
    await add('literal');
    await add('literal');
    const inputs = fixture.nativeElement.querySelectorAll(
      'article input[type="text"]',
    ) as NodeListOf<HTMLInputElement>;
    inputs[0].value = 'first';
    inputs[0].dispatchEvent(new Event('input'));
    inputs[2].value = 'second';
    inputs[2].dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(plugins.map((plugin) => plugin.typeId)).toEqual(['literal', 'literal']);
    expect(plugins[0].instanceId).not.toBe(plugins[1].instanceId);
    expect(plugins.map((plugin) => plugin.options.search)).toEqual(['first', 'second']);
  });

  it('emits exact visible order, maintains configuration after disabling, and removes instances', async () => {
    await add('literal');
    await add('case');
    const literalId = plugins[0].instanceId;
    button('Move plugin 2 up').click();
    await fixture.whenStable();
    expect(plugins.map((plugin) => plugin.typeId)).toEqual(['case', 'literal']);
    const literalCard = fixture.nativeElement.querySelectorAll('article')[1] as HTMLElement;
    (literalCard.querySelector('input[type="checkbox"]') as HTMLInputElement).click();
    const input = literalCard.querySelector('input[type="text"]') as HTMLInputElement;
    input.value = 'still configured';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(plugins[1].enabled).toBe(false);
    expect(plugins[1].options.search).toBe('still configured');
    expect(plugins[1].instanceId).toBe(literalId);
    button('Remove plugin 1').click();
    await fixture.whenStable();
    expect(plugins).toHaveLength(1);
    expect(plugins[0].instanceId).toBe(literalId);
  });

  it('emits an optional base folder independently for each TMDB plugin', async () => {
    await add('tmdb');
    await add('tmdb');
    expect(plugins.map((plugin) => plugin.options.baseFolder)).toEqual(['', '']);
    expect(plugins[0].options.changeFolder).toBe(false);
    const card = fixture.nativeElement.querySelector('article') as HTMLElement;
    const input = (label: string) =>
      [...card.querySelectorAll('label')]
        .find((element) => element.textContent?.includes(label))!
        .querySelector('input')!;
    const changeFolder = input('Allow folder change');
    expect(fixture.nativeElement.textContent).not.toContain('Base folder');
    changeFolder.click();
    await fixture.whenStable();
    const baseFolder = input('Base folder');
    baseFolder.value = 'Library/Movies';
    baseFolder.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(plugins[0].options.changeFolder).toBe(true);
    expect(plugins[0].options.baseFolder).toBe('Library/Movies');
    expect(plugins[1].options.baseFolder).toBe('');
    expect(fixture.nativeElement.textContent).toContain('Relative to the library root');

    changeFolder.click();
    await fixture.whenStable();
    expect(plugins[0].options.changeFolder).toBe(false);
    expect(plugins[0].options.baseFolder).toBe('Library/Movies');
    expect(fixture.nativeElement.textContent).not.toContain('Base folder');
  });

  it('adds a plugin with one click, describes each type and stops at 32', async () => {
    const buttons = [...fixture.nativeElement.querySelectorAll('button[data-plugin-type]')];
    expect(buttons.map((b) => (b as HTMLElement).textContent?.trim())).toEqual([
      expect.stringContaining('Literal replacement'),
      expect.stringContaining('Case change'),
      expect.stringContaining('TMDB'),
    ]);
    expect(buttons[0].textContent).toContain('Replace a piece of text');
    await add('case');
    expect(document.activeElement?.textContent).toContain('1. Case change');
    for (let i = 1; i < 32; i++) await add('case');
    expect(plugins).toHaveLength(32);
    expect(buttons.every((candidate) => (candidate as HTMLButtonElement).disabled)).toBe(true);
  });

  it('shows friendly choice labels while emitting the original values', async () => {
    await add('case');
    await add('literal');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('UPPERCASE');
    expect(text).toContain('Title Case');
    expect(text).toContain('First match only');
    const select = fixture.nativeElement.querySelector('article select') as HTMLSelectElement;
    select.value = 'title';
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(plugins[0].options.mode).toBe('title');
  });

  it('keeps hidden TMDB values and offers locale suggestions', async () => {
    await add('tmdb');
    expect(fixture.nativeElement.querySelectorAll('datalist#tmdb-languages option').length).toBe(
      12,
    );
    expect(plugins[0].options.folderTemplate).toBe('{title} ({year})');
    expect(plugins[0].options.seasonFolderTemplate).toBe('Season {season}');
    expect(plugins[0].options.language).toBe('en-US');
  });

  it('emits a season folder option and template for each TMDB plugin', async () => {
    await add('tmdb');
    expect(plugins[0].options.seasonFolder).toBe(false);
    expect(plugins[0].options.seasonFolderTemplate).toBe('Season {season}');
    const card = fixture.nativeElement.querySelector('article') as HTMLElement;
    const input = (label: string) =>
      [...card.querySelectorAll('label')]
        .find((element) => element.textContent?.includes(label))!
        .querySelector('input')!;
    expect(fixture.nativeElement.textContent).not.toContain('Add season folder');
    input('Allow folder change').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).not.toContain('Season folder template');
    input('Add season folder').click();
    await fixture.whenStable();
    const template = input('Season folder template');
    template.value = 'Season {seasonNumber}';
    template.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(plugins[0].options.seasonFolder).toBe(true);
    expect(plugins[0].options.seasonFolderTemplate).toBe('Season {seasonNumber}');
    expect(fixture.nativeElement.textContent).toContain('Season {seasonNumber}');
  });

  it('previews TMDB templates with sample metadata as options change', async () => {
    await add('tmdb');
    const card = fixture.nativeElement.querySelector('article') as HTMLElement;
    const preview = () => card.querySelector('section')!.textContent!;
    expect(preview()).toContain('Sample Movie (2024).mkv');
    expect(preview()).toContain('Sample Show (2019).mkv');
    const input = (label: string) =>
      [...card.querySelectorAll('label')]
        .find((element) => element.textContent?.includes(label))!
        .querySelector('input')!;
    const template = input('Filename template');
    template.value = '';
    template.dispatchEvent(new Event('input'));
    input('Allow folder change').click();
    await fixture.whenStable();
    input('Add season folder').click();
    await fixture.whenStable();
    expect(preview()).toContain('Sample Movie (2024)/Sample Movie (2024).mkv');
    expect(preview()).toContain(
      'Sample Show (2019)/Season 02/Sample Show - S02E05 - The Example Episode.mkv',
    );
    template.value = '{title} {episode}';
    template.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(preview()).toContain('Episode is unavailable.');
  });

  it('limits the recipe to 32 plugins', async () => {
    for (let i = 0; i < 32; i++) await add('case');
    const addButton = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(plugins).toHaveLength(32);
    expect(fixture.nativeElement.textContent).toContain('Your renaming steps (32/32)');
    expect(addButton.disabled).toBe(true);
    addButton.disabled = false;
    addButton.click();
    await fixture.whenStable();
    expect(plugins).toHaveLength(32);
  });

  it('moves plugins down and disables moves past either end', async () => {
    await add('literal');
    await add('case');
    expect(button('Move plugin 1 up').disabled).toBe(true);
    expect(button('Move plugin 2 down').disabled).toBe(true);
    button('Move plugin 1 down').click();
    await fixture.whenStable();
    expect(plugins.map((plugin) => plugin.typeId)).toEqual(['case', 'literal']);
    expect(document.activeElement?.textContent).toContain('Literal replacement');
  });

  it('returns focus to the add button after removing the last plugin', async () => {
    await add('case');
    button('Remove plugin 1').click();
    await fixture.whenStable();
    expect(plugins).toEqual([]);
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('button'));
  });

  it('edits choice options and defaults missing option values', async () => {
    fixture.componentRef.setInput('catalog', [
      ...testCatalog,
      { typeId: 'regex', version: 1, name: 'Regular expression', defaultOptions: {} },
    ]);
    await fixture.whenStable();
    await add('case');
    const mode = fixture.nativeElement.querySelector('article select') as HTMLSelectElement;
    expect([...mode.options].map((option) => option.value)).toEqual(['upper', 'lower', 'title']);
    mode.value = 'title';
    mode.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(plugins[0].options.mode).toBe('title');
    await add('regex');
    const regex = fixture.nativeElement.querySelectorAll('article')[1] as HTMLElement;
    const texts = [...regex.querySelectorAll('input[type="text"]')] as HTMLInputElement[];
    expect(texts.map((input) => input.value)).toEqual(['', '']);
    const checkboxes = regex.querySelectorAll('input[type="checkbox"]');
    expect((checkboxes[checkboxes.length - 1] as HTMLInputElement).checked).toBe(false);
    texts[0].value = '(\\d+)';
    texts[0].dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(plugins[1].options).toEqual({ pattern: '(\\d+)', replacement: '', ignoreCase: false });
  });

  it('accepts catalog types without editable options', async () => {
    fixture.componentRef.setInput('catalog', [
      { typeId: 'future', version: 2, name: 'Future plugin', defaultOptions: { mode: 'x' } },
    ]);
    await fixture.whenStable();
    await add('future');
    expect(plugins).toEqual([
      expect.objectContaining({ typeId: 'future', version: 2, options: { mode: 'x' } }),
    ]);
    expect(fixture.nativeElement.querySelector('article').textContent).toContain('Future plugin');
  });

  async function add(type: string): Promise<void> {
    (
      fixture.nativeElement.querySelector(`button[data-plugin-type="${type}"]`) as HTMLButtonElement
    ).click();
    await fixture.whenStable();
  }

  function button(label: string): HTMLButtonElement {
    return fixture.nativeElement.querySelector(`button[aria-label="${label}"]`);
  }
});
