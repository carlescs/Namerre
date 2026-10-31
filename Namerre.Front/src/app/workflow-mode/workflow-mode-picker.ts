import { Component, inject } from '@angular/core';
import { WorkflowMode, type WorkflowModeValue } from './workflow-mode';

@Component({
  selector: 'app-workflow-mode-picker',
  styleUrl: './workflow-mode-picker.scss',
  templateUrl: './workflow-mode-picker.html',
})
export class WorkflowModePicker {
  protected readonly workflow = inject(WorkflowMode);
  protected readonly options: readonly {
    value: WorkflowModeValue;
    label: string;
    title: string;
  }[] = [
    {
      value: 'simple',
      label: 'Simple',
      title: 'Simple mode: pick a preset and rename',
    },
    {
      value: 'advanced',
      label: 'Advanced',
      title: 'Advanced mode: build your own steps',
    },
  ];
}
