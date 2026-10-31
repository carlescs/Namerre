import { TestBed } from '@angular/core/testing';
import { SettingsMenu } from './settings-menu';

describe('SettingsMenu', () => {
  afterEach(() => localStorage.clear());

  async function render() {
    const fixture = TestBed.createComponent(SettingsMenu);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, trigger: element.querySelector('button') as HTMLButtonElement };
  }

  it('toggles a panel with the workflow mode and theme pickers', async () => {
    const { fixture, element, trigger } = await render();
    document.body.appendChild(element);
    expect(trigger.getAttribute('aria-label')).toBe('Options');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(element.querySelector('#settings-menu')).toBeNull();

    trigger.click();
    await fixture.whenStable();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(element.querySelector('#settings-menu app-workflow-mode-picker')).not.toBeNull();
    expect(element.querySelector('#settings-menu app-theme-picker')).not.toBeNull();
    await vi.waitFor(() =>
      expect((document.activeElement as HTMLElement).textContent?.trim()).toBe('Simple'),
    );

    trigger.click();
    await fixture.whenStable();
    expect(element.querySelector('#settings-menu')).toBeNull();
    element.remove();
  });

  it('stays open while choosing and closes on Escape, returning focus', async () => {
    const { fixture, element, trigger } = await render();
    document.body.appendChild(element);
    trigger.click();
    await fixture.whenStable();
    element.querySelector<HTMLButtonElement>('app-theme-picker button')!.click();
    await fixture.whenStable();
    expect(element.querySelector('#settings-menu')).not.toBeNull();

    element
      .querySelector('#settings-menu')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();
    expect(element.querySelector('#settings-menu')).toBeNull();
    expect(document.activeElement).toBe(trigger);
    element.remove();
  });

  it('closes when clicking elsewhere on the page', async () => {
    const { fixture, element, trigger } = await render();
    trigger.click();
    await fixture.whenStable();
    document.body.click();
    await fixture.whenStable();
    expect(element.querySelector('#settings-menu')).toBeNull();
  });
});
