import { Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

/** Shared loading / empty / error state, so every screen handles them the same way. */
@Component({
  selector: 'app-state-panel',
  imports: [MatIconModule, MatButtonModule, MatProgressSpinnerModule],
  template: `
    <div class="panel" [class.error]="kind() === 'error'">
      @switch (kind()) {
        @case ('loading') { <mat-spinner diameter="40" /> }
        @case ('error') { <mat-icon class="icon">error_outline</mat-icon> }
        @default { <mat-icon class="icon">{{ icon() }}</mat-icon> }
      }
      <div class="message">{{ message() }}</div>
      @if (kind() === 'error') {
        <button mat-stroked-button (click)="retry.emit()"><mat-icon>refresh</mat-icon> נסו שוב</button>
      }
      <ng-content />
    </div>
  `,
  styles: `
    .panel { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 48px 16px; color: #5f6b7a; text-align: center; }
    .panel.error { color: #a4262c; }
    .icon { font-size: 44px; width: 44px; height: 44px; opacity: .8; }
    .message { font-size: 15px; max-width: 520px; }
  `
})
export class StatePanel {
  readonly kind = input<'loading' | 'empty' | 'error'>('empty');
  readonly message = input('');
  readonly icon = input('inbox');
  readonly retry = output<void>();
}
