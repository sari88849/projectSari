import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, filter, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { errorMessage } from '../../core/errors';
import { MetadataStore } from '../../core/metadata.store';
import { ValidationSummary } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { ConfirmDialog, ConfirmData } from '../../shared/confirm-dialog';
import { StatePanel } from '../../shared/state-panel';
import { StatusBadge } from '../../shared/status-badge';

@Component({
  selector: 'app-validation-list',
  imports: [
    FormsModule, RouterLink, DatePipe, DecimalPipe, MatTableModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatTooltipModule, MatProgressSpinnerModule, StatusBadge, StatePanel
  ],
  templateUrl: './validation-list.html',
  styleUrl: './validation-list.scss'
})
export class ValidationList {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly notify = inject(NotifyService);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly metadata = inject(MetadataStore);

  protected readonly columns = ['name', 'entity', 'status', 'violations', 'lastRun', 'actions'];
  protected readonly items = signal<ValidationSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly running = signal<ReadonlySet<number>>(new Set());

  protected search = '';
  protected entity = '';
  private readonly searchChanges = new Subject<string>();

  protected hasFilter(): boolean {
    return this.search.trim() !== '' || this.entity !== '';
  }

  constructor() {
    this.searchChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
    this.load();
  }

  protected onSearch(value: string): void {
    this.searchChanges.next(value);
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.listValidations(this.search.trim(), this.entity).subscribe({
      next: items => { this.items.set(items); this.loading.set(false); },
      error: err => { this.error.set(errorMessage(err)); this.loading.set(false); }
    });
  }

  protected isRunning(id: number): boolean {
    return this.running().has(id);
  }

  protected execute(item: ValidationSummary): void {
    this.running.update(s => new Set(s).add(item.id));
    this.api.execute(item.id).subscribe({
      next: run => {
        this.running.update(s => { const n = new Set(s); n.delete(item.id); return n; });
        if (run.status === 'Succeeded') {
          this.notify.success(`הבדיקה "${item.name}" הורצה: ${run.violationCount} הפרות`);
          this.router.navigate(['/runs', run.runId]);
        } else {
          this.notify.error(run.errorMessage ?? 'הריצה נכשלה');
          this.load();
        }
      },
      error: err => {
        this.running.update(s => { const n = new Set(s); n.delete(item.id); return n; });
        this.notify.error(errorMessage(err));
      }
    });
  }

  protected remove(item: ValidationSummary): void {
    this.dialog.open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, {
      data: { title: 'מחיקת בדיקה', message: `למחוק את הבדיקה "${item.name}" ואת היסטוריית הריצות שלה?`, confirmText: 'מחיקה' },
      direction: 'rtl'
    }).afterClosed().pipe(
      filter(Boolean),
      switchMap(() => this.api.deleteValidation(item.id))
    ).subscribe({
      next: () => { this.notify.success('הבדיקה נמחקה'); this.load(); },
      error: err => this.notify.error(errorMessage(err))
    });
  }
}
