import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { PluginEditorComponent } from '../plugin-editor/plugin-editor.component';
import { PluginConfigurationComponent } from './plugin-configuration.component';

describe('PluginConfigurationComponent', () => {
  it('reports catalog failures and forwards plugin edits without requiring the wizard', async () => {
    await TestBed.configureTestingModule({
      imports: [PluginConfigurationComponent],
    }).compileComponents();
    const fixture = TestBed.createComponent(PluginConfigurationComponent);
    const retry = vi.fn();
    const changed = vi.fn();
    fixture.componentInstance.retryRequested.subscribe(retry);
    fixture.componentInstance.pluginsChanged.subscribe(changed);
    fixture.componentRef.setInput('error', 'Cannot load plugins.');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Cannot load plugins.',
    );
    fixture.nativeElement.querySelector('button').click();
    expect(retry).toHaveBeenCalledOnce();
    fixture.componentRef.setInput('error', '');
    fixture.componentRef.setInput('loaded', true);
    await fixture.whenStable();
    const editor = fixture.debugElement.query(By.directive(PluginEditorComponent))
      .componentInstance as PluginEditorComponent;
    editor.pluginsChanged.emit([]);
    expect(changed).toHaveBeenCalledWith([]);
    fixture.destroy();
  });
});
