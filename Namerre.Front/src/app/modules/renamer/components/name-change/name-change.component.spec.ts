import { TestBed } from '@angular/core/testing';
import { NameChangeComponent } from './name-change.component';

describe('NameChangeComponent', () => {
  async function render(source: string, target: string, side: 'from' | 'to') {
    await TestBed.configureTestingModule({ imports: [NameChangeComponent] }).compileComponents();
    const fixture = TestBed.createComponent(NameChangeComponent);
    fixture.componentRef.setInput('sourcePath', source);
    fixture.componentRef.setInput('targetPath', target);
    fixture.componentRef.setInput('side', side);
    await fixture.whenStable();
    return fixture;
  }

  it('highlights the new part and shows the folder only when it changes', async () => {
    const fixture = await render('in/a.mkv', 'out/Movie.mkv', 'to');
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(fixture.nativeElement.querySelector('mark').textContent).toBe('Movie');
    expect(text).toContain('Movie.mkv');
    expect(text).toContain('moves to out');
    fixture.destroy();
  });

  it('shows the old name with its folder as a muted line', async () => {
    const fixture = await render('in/a.mkv', 'in/Movie.mkv', 'from');
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(fixture.nativeElement.querySelector('mark').textContent).toBe('a');
    expect(text).toContain('in in');
    expect(text).not.toContain('moves to');
    fixture.destroy();
  });

  it('renders names exactly, keeping repeated spaces', async () => {
    const fixture = await render('A  B.mkv', 'A B.mkv', 'from');
    const name = fixture.nativeElement.querySelector('div') as HTMLElement;
    expect(name.textContent).toBe('A  B.mkv');
    expect(name.classList).toContain('whitespace-pre-wrap');
    fixture.destroy();
  });

  it('does not highlight or mention a folder when nothing changes', async () => {
    const fixture = await render('a.mkv', 'a.mkv', 'to');
    expect(fixture.nativeElement.querySelector('mark')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('moves to');
    fixture.destroy();
  });
});
