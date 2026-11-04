import { type ComponentFixture, TestBed } from '@angular/core/testing';
import type { SelectionPreview } from '../../rename-models';
import { RunUndoReviewComponent, type UndoConfirmation } from './run-undo-review.component';

describe('RunUndoReviewComponent', () => {
  let fixture: ComponentFixture<RunUndoReviewComponent>;
  let confirmed: UndoConfirmation[];
  const review: SelectionPreview = {
    files: [
      { id: 'a', safe: true, currentPath: 'A.txt', destinationPath: 'a.txt', issues: [] },
      {
        id: 'b',
        safe: false,
        currentPath: 'B.txt',
        destinationPath: 'b.txt',
        issues: [{ code: 'DestinationExists', message: 'Original destination is occupied.' }],
      },
      { id: 'c', safe: true, currentPath: 'C.txt', destinationPath: 'c.txt', issues: [] },
    ],
  };
  const button = () => fixture.nativeElement.querySelector('button') as HTMLButtonElement;
  const cleanup = () =>
    fixture.nativeElement.querySelector('input[type="checkbox"]') as HTMLInputElement;

  beforeEach(async () => {
    confirmed = [];
    fixture = TestBed.createComponent(RunUndoReviewComponent);
    fixture.componentRef.setInput('review', review);
    fixture.componentRef.setInput('runDeleteEmptyFolders', true);
    fixture.componentInstance.confirmed.subscribe((choice) => confirmed.push(choice));
    await fixture.whenStable();
  });

  it('counts and sends only the safe files', () => {
    expect(fixture.nativeElement.querySelector('h3').textContent).toBe(
      'Undo review — 2 files can be undone',
    );
    expect(fixture.nativeElement.textContent).toContain('Original destination is occupied.');
    button().click();
    expect(confirmed).toEqual([{ fileIds: ['a', 'c'], deleteEmptyFolders: true }]);
  });

  it('starts each review from the rename choice and sends a changed one', async () => {
    expect(cleanup().checked).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('The rename had this on.');
    cleanup().click();
    await fixture.whenStable();
    button().click();
    expect(confirmed[0].deleteEmptyFolders).toBe(false);

    fixture.componentRef.setInput('review', { files: [...review.files] });
    await fixture.whenStable();
    expect(cleanup().checked).toBe(true);
  });

  it('cannot be confirmed while busy or with nothing safe', async () => {
    fixture.componentRef.setInput('busy', true);
    await fixture.whenStable();
    expect(button().disabled).toBe(true);
    expect(cleanup().disabled).toBe(true);
    fixture.componentRef.setInput('busy', false);
    fixture.componentRef.setInput('review', { files: [review.files[1]] });
    await fixture.whenStable();
    expect(button().disabled).toBe(true);
    button().click();
    expect(confirmed).toEqual([]);
  });
});
