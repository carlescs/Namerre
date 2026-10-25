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
});
