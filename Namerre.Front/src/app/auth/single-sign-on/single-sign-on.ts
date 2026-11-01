import { Component, DOCUMENT, inject, signal } from '@angular/core';
import { Auth } from '../auth';

const errors: Record<string, string> = {
  'access-denied':
    'Your account is not allowed to use Namerre. Ask your identity provider administrator for access.',
  'not-linked':
    'Your single sign-on identity is not linked to a Namerre user yet. Ask a Namerre administrator to link it.',
  unavailable: 'The identity provider is unavailable. Try again later.',
};

@Component({
  selector: 'app-single-sign-on',
  templateUrl: './single-sign-on.html',
})
export class SingleSignOn {
  protected readonly auth = inject(Auth);
  protected readonly error = signal('');
  protected readonly redirecting = signal(false);

  constructor() {
    // The server reports a failed identity provider sign-in through a query parameter on `/`.
    const document = inject(DOCUMENT);
    const url = new URL(document.location.href);
    const code = url.searchParams.get('signInError');
    if (code !== null) {
      this.error.set(errors[code] ?? 'Single sign-on failed. Try again.');
      url.searchParams.delete('signInError');
      document.defaultView?.history.replaceState(null, '', url.pathname + url.search + url.hash);
    }
  }

  protected signIn(): void {
    this.redirecting.set(true);
    this.auth.startSingleSignOn();
  }
}
