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
