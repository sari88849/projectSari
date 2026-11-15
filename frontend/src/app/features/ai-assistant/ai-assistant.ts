import { PercentPipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { definitionErrors, errorMessage } from '../../core/errors';
import { MetadataStore } from '../../core/metadata.store';
import { AiProposal, BuilderDraft } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { StatePanel } from '../../shared/state-panel';

/**
 * Natural language -> AI proposal -> user review -> approve (save / save & run) or refine in the builder.
 * Nothing is saved or executed without an explicit user action.
 */
@Component({
  selector: 'app-ai-assistant',
  imports: [
    FormsModule, PercentPipe, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatIconModule,
    MatChipsModule, MatExpansionModule, MatProgressBarModule, StatePanel
  ],
  templateUrl: './ai-assistant.html',
  styleUrl: './ai-assistant.scss'
})
export class AiAssistant implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  protected readonly metadata = inject(MetadataStore);

  protected readonly examples = [
    'עובדים מתחת לגיל 18 עם שכר חודשי מעל 15000',
    'עובדים שתאריך סיום העסקה מוקדם מתאריך תחילת העסקה',
    'עובדים פעילים במחלקות לא פעילות',
    'עובדים שחסר להם תעודת זהות או דואר אלקטרוני או שיוך למחלקה או תאריך לידה',
    'עובדים עם שכר חודשי גבוה משכר מקסימלי לתפקיד',
    'עובדים פעילים שהמנהל הישיר שלהם לא פעיל'
  ];

  /** Optional deep link: /ai?q=... pre-fills the request and asks for a proposal (still requires approval). */
  readonly q = input<string>();

  protected prompt = '';
  protected rootEntity = '';
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly proposal = signal<AiProposal | null>(null);
  protected readonly saving = signal(false);
  protected name = '';
  protected description = '';

  ngOnInit(): void {
    const q = this.q();
    if (q?.trim()) {
      this.prompt = q;
      this.propose();
    }
  }

  protected useExample(text: string): void {
    this.prompt = text;
    this.propose();
  }

  protected propose(): void {
    if (!this.prompt.trim()) return;
    this.loading.set(true);
    this.error.set(null);
    this.proposal.set(null);
    this.api.propose(this.prompt.trim(), this.rootEntity || null).subscribe({
      next: p => {
        this.proposal.set(p);
        this.name = p.name ?? '';
        this.description = p.description ?? '';
        this.loading.set(false);
      },
      error: err => { this.error.set(errorMessage(err)); this.loading.set(false); }
    });
  }

  protected approve(run: boolean): void {
    const p = this.proposal();
    if (!p?.isValid || !p.definition || !this.name.trim()) return;
    this.saving.set(true);
    this.api.createValidation({ name: this.name.trim(), description: this.description.trim() || null, definition: p.definition, source: 'AI' })
      .pipe(switchMap(saved => run ? this.api.execute(saved.id) : of(null)))
      .subscribe({
        next: result => {
          this.saving.set(false);
          this.notify.success('הבדיקה אושרה ונשמרה');
          this.router.navigate(result ? ['/runs', result.runId] : ['/']);
        },
        error: err => {
          this.saving.set(false);
          const details = definitionErrors(err).map(e => e.message).join('; ');
          this.notify.error(details || errorMessage(err));
        }
      });
  }

  protected editInBuilder(): void {
    const p = this.proposal();
    if (!p?.definition) return;
    const draft: BuilderDraft = { name: this.name, description: this.description, definition: p.definition, source: 'AI' };
    this.router.navigate(['/validations/new'], { state: { draft } });
  }

  protected prettyJson(raw: string): string {
    try { return JSON.stringify(JSON.parse(raw), null, 2); } catch { return raw; }
  }
}
