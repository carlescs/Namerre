import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { UserMenu } from './user-menu';

describe('UserMenu', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UserMenu],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(UserMenu);
    fixture.componentRef.setInput('username', 'admin');
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, trigger: element.querySelector('button') as HTMLButtonElement };
  }

  it('shows the username and toggles a menu with change password', async () => {
    const { fixture, element, trigger } = await render();
    document.body.appendChild(element);
    expect(trigger.textContent).toContain('admin');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(element.querySelector('[role="menu"]')).toBeNull();

    trigger.click();
    await fixture.whenStable();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    const item = element.querySelector('[role="menuitem"]') as HTMLAnchorElement;
    expect(item.textContent).toContain('Change password');
    expect(item.getAttribute('href')).toBe('/account');
    await vi.waitFor(() => expect(document.activeElement).toBe(item));

    trigger.click();
    await fixture.whenStable();
    expect(element.querySelector('[role="menu"]')).toBeNull();
    element.remove();
  });

  it('closes on Escape and returns focus to the button', async () => {
    const { fixture, element, trigger } = await render();
    document.body.appendChild(element);
    trigger.click();
    await fixture.whenStable();
    element
      .querySelector('[role="menuitem"]')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();
    expect(element.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
    element.remove();
  });

  it('closes when clicking elsewhere on the page', async () => {
    const { fixture, element, trigger } = await render();
    trigger.click();
    await fixture.whenStable();
    document.body.click();
    await fixture.whenStable();
    expect(element.querySelector('[role="menu"]')).toBeNull();
  });

  it('signs out from the menu and moves between items with the arrow keys', async () => {
    const { fixture, element, trigger } = await render();
    document.body.appendChild(element);
    const signOut = vi.fn();
    fixture.componentInstance.signOut.subscribe(signOut);
    trigger.click();
    await fixture.whenStable();
    const items = [...element.querySelectorAll<HTMLElement>('[role="menuitem"]')];
    expect(items.map((item) => item.textContent?.trim())).toEqual(['Change password', 'Sign out']);
    await vi.waitFor(() => expect(document.activeElement).toBe(items[0]));
    items[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(items[1]);
    items[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    expect(document.activeElement).toBe(items[0]);

    items[1].click();
    await fixture.whenStable();
    expect(signOut).toHaveBeenCalledOnce();
    expect(element.querySelector('[role="menu"]')).toBeNull();
    element.remove();
  });

  it('disables sign out while signing out', async () => {
    const { fixture, element, trigger } = await render();
    fixture.componentRef.setInput('signingOut', true);
    trigger.click();
    await fixture.whenStable();
    const item = element.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')[1];
    expect(item.disabled).toBe(true);
    expect(item.textContent).toContain('Signing out...');
  });
});
