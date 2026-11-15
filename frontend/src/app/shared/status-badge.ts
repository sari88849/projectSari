import { Component, computed, input } from '@angular/core';
import { RunStatus } from '../core/models';

/** Colored chip for a run status ("לא הורץ" when there is no run yet). */
@Component({
  selector: 'app-status-badge',
  template: `<span class="badge" [class]="'badge ' + css()">{{ label() }}</span>`,
  styles: `
    .badge { display: inline-block; padding: 2px 10px; border-radius: 12px; font-size: 12px; font-weight: 500; white-space: nowrap; }
    .ok { background: #e3f4e8; color: #1b6e35; }
    .violations { background: #fdecea; color: #a4262c; }
    .failed { background: #fff4e5; color: #8a4b00; }
    .running { background: #e8f0fe; color: #1a56c4; }
    .none { background: #eceff1; color: #546e7a; }
  `
})
export class StatusBadge {
  readonly status = input<RunStatus | null | undefined>();
  /** Violation count: a succeeded run with violations is shown in red. */
  readonly violations = input<number | null | undefined>();

  protected readonly css = computed(() => {
    switch (this.status()) {
      case 'Succeeded': return (this.violations() ?? 0) > 0 ? 'violations' : 'ok';
      case 'Failed': return 'failed';
      case 'Running': return 'running';
      default: return 'none';
    }
  });

  protected readonly label = computed(() => {
    switch (this.status()) {
      case 'Succeeded': return (this.violations() ?? 0) > 0 ? 'נמצאו הפרות' : 'תקין';
      case 'Failed': return 'הריצה נכשלה';
      case 'Running': return 'בריצה';
      default: return 'לא הורץ';
    }
  });
}
