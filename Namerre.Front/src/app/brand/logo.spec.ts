import { TestBed } from '@angular/core/testing';
import { Logo } from './logo';

describe('Logo', () => {
  it('is decorative and gives each instance its own gradient', async () => {
    const first = TestBed.createComponent(Logo);
    const second = TestBed.createComponent(Logo);
    await first.whenStable();
    await second.whenStable();
    const host = first.nativeElement as HTMLElement;
    expect(host.getAttribute('aria-hidden')).toBe('true');

    const firstId = host.querySelector('linearGradient')!.id;
    expect(firstId).not.toBe(second.nativeElement.querySelector('linearGradient').id);
    expect(host.querySelector('rect')!.getAttribute('fill')).toBe(`url(#${firstId})`);
  });
});
