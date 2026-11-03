/** Opens a dialog as a modal where supported, else as a plain open dialog. */
export function openModal(dialog: HTMLDialogElement): void {
  if (dialog.hasAttribute('open')) return;
  if (typeof dialog.showModal === 'function') dialog.showModal();
  else dialog.setAttribute('open', '');
}

/** Closes a dialog opened with `openModal`; the browser then returns focus to where it was. */
export function closeModal(dialog: HTMLDialogElement): void {
  if (typeof dialog.close === 'function') dialog.close();
  else dialog.removeAttribute('open');
}
