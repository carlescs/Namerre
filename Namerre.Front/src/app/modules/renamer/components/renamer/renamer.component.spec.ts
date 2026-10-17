import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RenamerComponent } from './renamer.component';

describe('RenamerComponent', () => {
  let component: RenamerComponent;
  let fixture: ComponentFixture<RenamerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RenamerComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(RenamerComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
