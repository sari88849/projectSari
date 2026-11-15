import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { Observable, Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { cleanDefinition, countConditions, newDefinition, normalizeDefinition } from '../../core/definition.utils';
import { definitionErrors, errorMessage } from '../../core/errors';
import { MetadataStore } from '../../core/metadata.store';
import { BuilderDraft, DefinitionError, DefinitionPreview, FieldDto, SaveValidationRequest, ValidationDefinition, ValidationDto } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { StatePanel } from '../../shared/state-panel';
import { ConditionGroupEditor } from './condition-group-editor';

@Component({
  selector: 'app-validation-builder',
  imports: [
    FormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule, MatIconModule,
    MatExpansionModule, MatProgressBarModule, MatTooltipModule, ConditionGroupEditor, StatePanel
  ],
  templateUrl: './validation-builder.html',
  styleUrl: './validation-builder.scss'
})
export class ValidationBuilder implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly metadata = inject(MetadataStore);

  /** Route parameter (edit mode); bound via withComponentInputBinding. */
  readonly id = input<string>();

  protected name = '';
  protected description = '';
  protected source: 'Manual' | 'AI' = 'Manual';
  protected readonly definition = signal<ValidationDefinition>(newDefinition());
  protected readonly fields = signal<FieldDto[]>([]);
  protected readonly preview = signal<DefinitionPreview | null>(null);
  protected readonly previewLoading = signal(false);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveErrors = signal<DefinitionError[]>([]);
  protected readonly submitted = signal(false);

  private readonly changes = new Subject<void>();

  protected readonly isEdit = computed(() => !!this.id());
  protected readonly rootLabel = computed(() =>
    this.metadata.entities().find(e => e.key === this.definition().rootEntity)?.displayName ?? '');

  /** Errors per condition id, shown under the matching row. */
  protected readonly errorsByNode = computed(() => {
    const map = new Map<string, string[]>();
    const all = [...(this.preview()?.errors ?? []), ...this.saveErrors()];
    for (const e of all) {
      if (!e.nodeId) continue;
      if (!this.submitted() && !this.touched(e.nodeId)) continue;
      map.set(e.nodeId, [...(map.get(e.nodeId) ?? []), e.message]);
    }
    return map;
  });

  protected readonly generalErrors = computed(() =>
    [...(this.preview()?.errors ?? []), ...this.saveErrors()].filter(e => !e.nodeId).map(e => e.message));

  constructor() {
    // Live preview: every edit is validated by the backend (same validator as save/execute), debounced.
    this.changes.pipe(
      debounceTime(400),
      switchMap(() => this.requestPreview()),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(p => { this.preview.set(p); this.previewLoading.set(false); });
  }

  ngOnInit(): void {
    const id = this.id();
    if (id) {
      this.loadValidation(Number(id));
      return;
    }
    const draft = (history.state as { draft?: BuilderDraft } | null)?.draft;
    if (draft) {
      this.name = draft.name;
      this.description = draft.description ?? '';
      this.source = draft.source;
      this.setDefinition(normalizeDefinition(draft.definition));
    }
  }

  protected loadValidation(id: number): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.getValidation(id).subscribe({
      next: (v: ValidationDto) => {
        this.name = v.name;
        this.description = v.description ?? '';
        this.source = v.source;
        this.setDefinition(normalizeDefinition(v.definition));
        this.loading.set(false);
      },
      error: err => { this.loadError.set(errorMessage(err)); this.loading.set(false); }
    });
  }

  private setDefinition(definition: ValidationDefinition): void {
    this.definition.set(definition);
    this.loadFields(definition.rootEntity);
    this.onChanged();
  }

  private loadFields(entity: string): void {
    this.fields.set([]);
    this.metadata.fields(entity).subscribe({
      next: f => this.fields.set(f),
      error: err => this.notify.error(errorMessage(err))
    });
  }

  protected onEntityChange(entity: string): void {
    // Field paths are relative to the root entity, so changing it starts a new condition tree.
    const display: string[] = [];
    this.definition.set({ ...newDefinition(entity), displayFields: display });
    this.submitted.set(false);
    this.saveErrors.set([]);
    this.loadFields(entity);
    this.onChanged();
  }

  /** Called on any edit in the tree (objects are edited in place; the signal is re-set to notify). */
  protected onChanged(): void {
    this.definition.set({ ...this.definition() });
    this.saveErrors.set([]);
    if (!this.definition().rootEntity) { this.preview.set(null); return; }
    this.previewLoading.set(true);
    this.changes.next();
  }

  protected setDisplayFields(paths: string[]): void {
    this.definition().displayFields = paths;
    this.onChanged();
  }

  private requestPreview(): Observable<DefinitionPreview | null> {
    return this.api.preview(cleanDefinition(this.definition())).pipe(catchError(() => of(null)));
  }

  private touched(nodeId: string): boolean {
    // Show row errors only once the user started filling that row (or tried to save).
    const find = (n: any): any => n.id === nodeId ? n : n.children?.map(find).find(Boolean);
    const node = find(this.definition().condition);
    return !!node && node.type === 'condition' && !!node.field;
  }

  protected conditionCount(): number {
    return countConditions(this.definition().condition);
  }

  protected canSave(): boolean {
    return !this.saving() && !!this.name.trim() && !!this.definition().rootEntity;
  }

  protected save(runAfterSave: boolean): void {
    this.submitted.set(true);
    if (!this.canSave()) return;
    const request: SaveValidationRequest = {
      name: this.name.trim(),
      description: this.description.trim() || null,
      definition: cleanDefinition(this.definition()),
      source: this.source
    };
    const id = this.id();
    const call = id ? this.api.updateValidation(Number(id), request) : this.api.createValidation(request);

    this.saving.set(true);
    call.pipe(
      switchMap(saved => runAfterSave ? this.api.execute(saved.id) : of(null))
    ).subscribe({
      next: run => {
        this.saving.set(false);
        this.notify.success('הבדיקה נשמרה');
        if (run) this.router.navigate(['/runs', run.runId]);
        else this.router.navigate(['/']);
      },
      error: err => {
        this.saving.set(false);
        const errors = definitionErrors(err);
        this.saveErrors.set(errors.length ? errors : [{ message: errorMessage(err) }]);
        this.notify.error(errorMessage(err));
      }
    });
  }
}
