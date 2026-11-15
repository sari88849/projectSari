import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, of, shareReplay, tap } from 'rxjs';
import { ApiService } from './api.service';
import { DataType, EntityDto, FieldDto, OperatorDto } from './models';

/**
 * Caches business metadata (entities, operators, fields per entity) for the whole app.
 * Metadata changes rarely, so it is loaded once per session.
 */
@Injectable({ providedIn: 'root' })
export class MetadataStore {
  private readonly api = inject(ApiService);
  private readonly fieldCache = new Map<string, Observable<FieldDto[]>>();

  readonly entities = signal<EntityDto[]>([]);
  readonly operators = signal<OperatorDto[]>([]);
  readonly loaded = signal(false);
  readonly error = signal<unknown>(null);

  readonly entityLabels = computed(() => new Map(this.entities().map(e => [e.key, e.displayNamePlural])));

  constructor() {
    this.load();
  }

  load(): void {
    this.error.set(null);
    this.api.entities().subscribe({
      next: e => {
        this.entities.set(e);
        this.api.operators().subscribe({
          next: o => { this.operators.set(o); this.loaded.set(true); },
          error: err => this.error.set(err)
        });
      },
      error: err => this.error.set(err)
    });
  }

  fields(entityKey: string): Observable<FieldDto[]> {
    if (!entityKey) return of([]);
    let cached = this.fieldCache.get(entityKey);
    if (!cached) {
      cached = this.api.fields(entityKey).pipe(
        tap({ error: () => this.fieldCache.delete(entityKey) }),
        shareReplay(1));
      this.fieldCache.set(entityKey, cached);
    }
    return cached;
  }

  operatorsFor(type: DataType | undefined): OperatorDto[] {
    return type ? this.operators().filter(o => o.supportedTypes.includes(type)) : [];
  }

  operatorLabel(op: OperatorDto, type?: DataType): string {
    return type === 'Date' && op.dateLabel ? op.dateLabel : op.label;
  }

  entityLabel(key: string): string {
    return this.entityLabels().get(key) ?? key;
  }
}
