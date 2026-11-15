import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { errorMessage } from '../../core/errors';
import { PagedResults, RunDto, ValidationDto } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { StatePanel } from '../../shared/state-panel';
import { StatusBadge } from '../../shared/status-badge';

/**
 * Results of one run: summary (count, status, duration), violating records with every failure reason,
 * server-side search, filter by failed condition, paging, and run history.
 */
@Component({
  selector: 'app-validation-results',
  imports: [
    FormsModule, RouterLink, DatePipe, DecimalPipe, MatTableModule, MatPaginatorModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatChipsModule, MatButtonModule, MatIconModule, MatProgressBarModule, MatExpansionModule,
    MatTooltipModule, StatusBadge, StatePanel
  ],
  templateUrl: './validation-results.html',
  styleUrl: './validation-results.scss'
})
export class ValidationResults {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly destroyRef = inject(DestroyRef);

  /** Route parameter. */
  readonly runId = input.required<string>();

  protected readonly run = signal<RunDto | null>(null);
  protected readonly validation = signal<ValidationDto | null>(null);
  protected readonly history = signal<RunDto[]>([]);
  protected readonly results = signal<PagedResults | null>(null);
  protected readonly runLoading = signal(true);
  protected readonly runError = signal<string | null>(null);
  protected readonly resultsLoading = signal(false);
  protected readonly resultsError = signal<string | null>(null);
  protected readonly rerunning = signal(false);

  protected search = '';
  protected readonly conditionFilter = signal<string | null>(null);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(25);
  private readonly searchChanges = new Subject<string>();

  protected readonly displayedColumns = computed(() =>
    ['row', 'record', ...(this.run()?.columns ?? []).map((_, i) => 'col' + i), 'reasons']);

  protected filtered(): boolean {
    return !!this.search.trim() || !!this.conditionFilter();
  }

  constructor() {
    // Reload whenever the route's run id changes (e.g. after re-run or choosing from history).
    effect(() => {
      const id = Number(this.runId());
      untracked(() => this.loadRun(id));
    });
    this.searchChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => { this.page.set(0); this.loadResults(); });
  }

  protected loadRun(id = Number(this.runId())): void {
    this.runLoading.set(true);
    this.runError.set(null);
    this.results.set(null);
    this.conditionFilter.set(null);
    this.search = '';
    this.page.set(0);
    this.api.getRun(id).subscribe({
      next: run => {
        this.run.set(run);
        this.runLoading.set(false);
        if (this.validation()?.id !== run.validationId) {
          this.api.getValidation(run.validationId).subscribe({ next: v => this.validation.set(v), error: () => this.validation.set(null) });
        }
        this.api.runs(run.validationId).subscribe({ next: h => this.history.set(h), error: () => this.history.set([]) });
        if (run.status === 'Succeeded') this.loadResults();
      },
      error: err => { this.runError.set(errorMessage(err)); this.runLoading.set(false); }
    });
  }

  protected loadResults(): void {
    const run = this.run();
    if (!run) return;
    this.resultsLoading.set(true);
    this.resultsError.set(null);
    this.api.results(run.runId, {
      search: this.search.trim(), conditionId: this.conditionFilter(), page: this.page() + 1, pageSize: this.pageSize()
    }).subscribe({
      next: r => { this.results.set(r); this.resultsLoading.set(false); },
      error: err => { this.resultsError.set(errorMessage(err)); this.resultsLoading.set(false); }
    });
  }

  protected onSearch(value: string): void {
    this.searchChanges.next(value);
  }

  protected setCondition(id: string | null | undefined): void {
    this.conditionFilter.set(id ?? null);
    this.page.set(0);
    this.loadResults();
  }

  protected clearFilters(): void {
    this.search = '';
    this.searchChanges.next('');
    this.conditionFilter.set(null);
    this.page.set(0);
    this.loadResults();
  }

  protected onPage(e: PageEvent): void {
    this.page.set(e.pageIndex);
    this.pageSize.set(e.pageSize);
    this.loadResults();
  }

  protected rerun(): void {
    const run = this.run();
    if (!run) return;
    this.rerunning.set(true);
    this.api.execute(run.validationId).subscribe({
      next: newRun => {
        this.rerunning.set(false);
        if (newRun.status === 'Failed') this.notify.error(newRun.errorMessage ?? 'הריצה נכשלה');
        this.router.navigate(['/runs', newRun.runId]);
      },
      error: err => { this.rerunning.set(false); this.notify.error(errorMessage(err)); }
    });
  }

  protected openRun(runId: number): void {
    this.router.navigate(['/runs', runId]);
  }

  protected conditionText(id: string): string {
    return this.run()?.conditions.find(c => c.id === id)?.description ?? id;
  }
}
