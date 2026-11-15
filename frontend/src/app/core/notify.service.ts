import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): void {
    this.snackBar.open(message, 'סגירה', { duration: 3500, panelClass: 'snack-success', direction: 'rtl' });
  }

  error(message: string): void {
    this.snackBar.open(message, 'סגירה', { duration: 6000, panelClass: 'snack-error', direction: 'rtl' });
  }
}
