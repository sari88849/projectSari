import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MetadataStore } from './core/metadata.store';
import { errorMessage } from './core/errors';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule],
  template: `
    <mat-toolbar class="topbar">
      <a routerLink="/" class="brand">
        <mat-icon>fact_check</mat-icon>
        <span>מערכת בדיקות נתונים</span>
      </a>
      <nav>
        <a mat-button routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
          <mat-icon>list</mat-icon> הבדיקות שלי
        </a>
        <a mat-button routerLink="/validations/new" routerLinkActive="active">
          <mat-icon>add_circle_outline</mat-icon> בדיקה חדשה
        </a>
        <a mat-button routerLink="/ai" routerLinkActive="active">
          <mat-icon>auto_awesome</mat-icon> יצירה בעזרת AI
        </a>
      </nav>
    </mat-toolbar>

    @if (metadata.error()) {
      <div class="global-error">
        {{ message(metadata.error()) }}
        <button mat-button (click)="metadata.load()">נסו שוב</button>
      </div>
    }

    <main class="page">
      <router-outlet />
    </main>
  `,
  styles: `
    .topbar { background: var(--brand); color: #fff; gap: 24px; position: sticky; top: 0; z-index: 10; flex-wrap: wrap; height: auto; min-height: 64px; }
    .brand { display: flex; align-items: center; gap: 8px; color: inherit; text-decoration: none; font-weight: 600; font-size: 18px; }
    nav { display: flex; gap: 4px; flex-wrap: wrap; }
    nav a { color: rgba(255,255,255,.85); }
    nav a.active { color: #fff; background: rgba(255,255,255,.14); }
    .global-error { background: #fdecea; color: #a4262c; padding: 8px 24px; display: flex; align-items: center; gap: 12px; }
    .page { max-width: 1280px; margin: 0 auto; padding: 24px 16px 48px; }
  `
})
export class App {
  protected readonly metadata = inject(MetadataStore);
  protected readonly message = errorMessage;
}
