import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'בדיקות נתונים',
    loadComponent: () => import('./features/validation-list/validation-list').then(m => m.ValidationList)
  },
  {
    path: 'validations/new',
    title: 'בדיקה חדשה',
    loadComponent: () => import('./features/validation-builder/validation-builder').then(m => m.ValidationBuilder)
  },
  {
    path: 'validations/:id/edit',
    title: 'עריכת בדיקה',
    loadComponent: () => import('./features/validation-builder/validation-builder').then(m => m.ValidationBuilder)
  },
  {
    path: 'runs/:runId',
    title: 'תוצאות בדיקה',
    loadComponent: () => import('./features/validation-results/validation-results').then(m => m.ValidationResults)
  },
  {
    path: 'ai',
    title: 'יצירת בדיקה בעזרת AI',
    loadComponent: () => import('./features/ai-assistant/ai-assistant').then(m => m.AiAssistant)
  },
  { path: '**', redirectTo: '' }
];
