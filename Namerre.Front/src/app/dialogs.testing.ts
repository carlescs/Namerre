type DialogMethod = (this: HTMLDialogElement) => void;

/**
 * Replaces `HTMLDialogElement` `showModal` and `close` for a test (jsdom implements neither) and returns a
 * function that puts back exactly what was there, so stubs never leak into other spec files.
 */
export function stubDialogs(showModal: DialogMethod, close: DialogMethod): () => void {
  const prototype = HTMLDialogElement.prototype;
  const names = ['showModal', 'close'] as const;
  const saved = names.map(
    (name) => [name, Object.getOwnPropertyDescriptor(prototype, name)] as const,
  );
  for (const [name, value] of [
    ['showModal', showModal],
    ['close', close],
  ] as const) {
    Object.defineProperty(prototype, name, { configurable: true, writable: true, value });
  }
  return () => {
    for (const [name, descriptor] of saved) {
      if (descriptor) Object.defineProperty(prototype, name, descriptor);
      else Reflect.deleteProperty(prototype, name);
    }
  };
}

/** Stubs that behave like a browser: `showModal` opens the dialog and `close` closes it. */
export function stubDialogsThatToggle(): () => void {
  return stubDialogs(
    function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    },
    function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    },
  );
}
