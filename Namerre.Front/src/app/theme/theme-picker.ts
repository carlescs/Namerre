import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCircleHalfStroke, faMoon, faSun } from '@fortawesome/free-solid-svg-icons';
import { Theme, ThemePreference } from './theme';

@Component({
  imports: [FaIconComponent],
  selector: 'app-theme-picker',
  styleUrl: './theme-picker.scss',
  templateUrl: './theme-picker.html',
})
export class ThemePicker {
  protected readonly theme = inject(Theme);
  protected readonly options: readonly {
    value: ThemePreference;
    label: string;
    icon: typeof faSun;
  }[] = [
    { value: 'light', label: 'Light theme', icon: faSun },
    { value: 'dark', label: 'Dark theme', icon: faMoon },
    { value: 'system', label: 'Match system theme', icon: faCircleHalfStroke },
  ];
}
