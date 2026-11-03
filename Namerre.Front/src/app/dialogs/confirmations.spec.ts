import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { stubDialogsThatToggle } from '../dialogs.testing';
import { ConfirmDialog } from './confirm-dialog';
import { ConfirmationHost } from './confirmation-host';
import { Confirmations } from './confirmations';

function buttonNamed(element: HTMLElement, text: string): HTMLButtonElement {
  const found = [...element.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === text,
  );
  if (!found) throw new Error(`No button ${text}`);
  return found;
}

describe('Confirmations', () => {
  let restoreDialogs: () => void;
  beforeEach(() => {
    restoreDialogs = stubDialogsThatToggle();
  });
  afterEach(() => restoreDialogs());

  async function render() {
    const fixture = TestBed.createComponent(ConfirmationHost);
    await fixture.whenStable();
    return {
      fixture,
      element: fixture.nativeElement as HTMLElement,
      confirmations: TestBed.inject(Confirmations),
    };
  }

  it('asks in a modal alert dialog and resolves true when confirmed', async () => {
    const { fixture, element, confirmations } = await render();
    expect(element.querySelector('dialog')).toBeNull();
    const answer = confirmations.ask({
      heading: 'Remove passkey Phone?',
      message: 'You will no longer be able to sign in with it.',
      confirmLabel: 'Remove passkey',
      danger: true,
    });
    await fixture.whenStable();
    const dialog = element.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.hasAttribute('open')).toBe(true);
    expect(dialog.getAttribute('role')).toBe('alertdialog');
    expect(dialog.querySelector('h2')?.textContent).toBe('Remove passkey Phone?');
    expect(
      document.getElementById(dialog.getAttribute('aria-describedby')!)?.textContent,
    ).toContain('You will no longer be able to sign in with it.');
    const confirm = buttonNamed(dialog, 'Remove passkey');
    expect(confirm.classList).toContain('btn-danger');
    expect(buttonNamed(dialog, 'Cancel').autofocus).toBe(true);
    confirm.click();
    await expect(answer).resolves.toBe(true);
    await fixture.whenStable();
    expect(element.querySelector('dialog')).toBeNull();
    expect(confirmations.current()).toBeNull();
  });

  it('resolves false when cancelled or dismissed with Escape', async () => {
    const { fixture, element, confirmations } = await render();
    let answer = confirmations.ask({ heading: 'Delete?', message: 'Gone for good.' });
    await fixture.whenStable();
    expect(buttonNamed(element, 'Confirm').classList).toContain('btn-primary');
    buttonNamed(element, 'Cancel').click();
    await expect(answer).resolves.toBe(false);

    answer = confirmations.ask({ heading: 'Delete?', message: 'Gone for good.' });
    await fixture.whenStable();
    element.querySelector('dialog')!.dispatchEvent(new Event('cancel', { cancelable: true }));
    await expect(answer).resolves.toBe(false);
  });

  it('cancels an earlier question when asked again, in a new dialog', async () => {
    const { fixture, element, confirmations } = await render();
    const first = confirmations.ask({ heading: 'First?', message: 'One.' });
    await fixture.whenStable();
    const firstDialog = element.querySelector('dialog');
    const second = confirmations.ask({ heading: 'Second?', message: 'Two.', danger: true });
    await expect(first).resolves.toBe(false);
    await fixture.whenStable();
    const secondDialog = element.querySelector('dialog') as HTMLDialogElement;
    // A new dialog, so the destructive question gets its own initial focus on Cancel.
    expect(secondDialog).not.toBe(firstDialog);
    expect(secondDialog.hasAttribute('open')).toBe(true);
    expect(secondDialog.querySelector('h2')?.textContent).toBe('Second?');
    expect(buttonNamed(secondDialog, 'Cancel').autofocus).toBe(true);
    confirmations.answer(true);
    await expect(second).resolves.toBe(true);
  });

  it('cancels the question when navigating away', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'other', children: [] }])],
    });
    const { fixture, element, confirmations } = await render();
    const answer = confirmations.ask({ heading: 'Delete alice?', message: 'Gone.' });
    await fixture.whenStable();
    await TestBed.inject(Router).navigateByUrl('/other');
    await expect(answer).resolves.toBe(false);
    await fixture.whenStable();
    expect(element.querySelector('dialog')).toBeNull();
  });

  it('cancels the question when the host goes away', async () => {
    const { fixture, confirmations } = await render();
    const answer = confirmations.ask({ heading: 'Delete alice?', message: 'Gone.' });
    fixture.destroy();
    await expect(answer).resolves.toBe(false);
    expect(confirmations.current()).toBeNull();
  });
});

@Component({
  imports: [ConfirmDialog],
  template: `
    @if (shown()) {
      <app-confirm-dialog
        heading="Delete 2 records?"
        confirmLabel="Delete"
        danger
        [busy]="busy()"
        (confirmed)="confirmed = confirmed + 1"
        (cancelled)="shown.set(false)"
      >
        <p>Files stay on disk.</p>
        @if (busy()) {
          <p role="status">Deleting…</p>
        }
      </app-confirm-dialog>
    }
  `,
})
class Parent {
  readonly shown = signal(true);
  readonly busy = signal(false);
  confirmed = 0;
}

describe('ConfirmDialog', () => {
  let restoreDialogs: () => void;
  beforeEach(() => {
    restoreDialogs = stubDialogsThatToggle();
  });
  afterEach(() => restoreDialogs());

  it('stays open for the parent to show progress, and blocks answers while busy', async () => {
    const fixture = TestBed.createComponent(Parent);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const dialog = element.querySelector('dialog') as HTMLDialogElement;
    expect(dialog.hasAttribute('open')).toBe(true);
    buttonNamed(dialog, 'Delete').click();
    expect(fixture.componentInstance.confirmed).toBe(1);

    fixture.componentInstance.busy.set(true);
    await fixture.whenStable();
    expect(dialog.getAttribute('aria-busy')).toBe('true');
    expect(dialog.querySelector('[role="status"]')?.textContent).toBe('Deleting…');
    expect(buttonNamed(dialog, 'Delete').disabled).toBe(true);
    expect(buttonNamed(dialog, 'Cancel').disabled).toBe(true);
    dialog.dispatchEvent(new Event('cancel', { cancelable: true }));
    expect(fixture.componentInstance.shown()).toBe(true);

    fixture.componentInstance.busy.set(false);
    await fixture.whenStable();
    buttonNamed(dialog, 'Cancel').click();
    await fixture.whenStable();
    expect(dialog.hasAttribute('open')).toBe(false);
    expect(element.querySelector('dialog')).toBeNull();
  });
});
