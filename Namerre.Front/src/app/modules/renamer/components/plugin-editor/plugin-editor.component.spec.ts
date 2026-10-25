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
    changeFolder.click();
    const baseFolder = input('Base folder');
    baseFolder.value = 'Library/Movies';
    baseFolder.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(plugins[0].options.changeFolder).toBe(true);
    expect(plugins[0].options.baseFolder).toBe('Library/Movies');
    expect(plugins[1].options.baseFolder).toBe('');
    expect(fixture.nativeElement.textContent).toContain('relative to the configured server root');

    changeFolder.click();
    await fixture.whenStable();
    expect(plugins[0].options.changeFolder).toBe(false);
    expect(plugins[0].options.baseFolder).toBe('Library/Movies');
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
    input('Add season folder').click();
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
    const template = input('Filename stem template');
    template.value = '';
    template.dispatchEvent(new Event('input'));
    input('Allow folder change').click();
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
    expect(fixture.nativeElement.textContent).toContain('Ordered plugins (32/32)');
    expect(addButton.disabled).toBe(true);
    addButton.disabled = false;
    addButton.click();
    await fixture.whenStable();
    expect(plugins).toHaveLength(32);
  });

  it('does not add a plugin before a type is chosen', async () => {
    const addButton = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(addButton.disabled).toBe(true);
    addButton.disabled = false;
    addButton.click();
    await fixture.whenStable();
    expect(plugins).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('No plugins selected');
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
    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = type;
    select.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    await fixture.whenStable();
  }

  function button(label: string): HTMLButtonElement {
    return fixture.nativeElement.querySelector(`button[aria-label="${label}"]`);
  }
});
