import { Component } from '@angular/core';

let nextId = 0;

/** The Namerre mark, kept in sync with `public/favicon.svg`. Size it with a class on the host. */
@Component({
  selector: 'app-logo',
  styleUrl: './logo.scss',
  templateUrl: './logo.html',
  host: { 'aria-hidden': 'true' },
})
export class Logo {
  protected readonly gradientId = `app-logo-gradient-${nextId++}`;
}
