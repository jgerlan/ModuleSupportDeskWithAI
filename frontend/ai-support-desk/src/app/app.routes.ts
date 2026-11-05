import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: 'dashboard', title: 'Dashboard | Support Desk', loadComponent: () => import('./features/dashboard/dashboard').then(m => m.Dashboard) },
  { path: 'tickets', title: 'Tickets | Support Desk', loadComponent: () => import('./features/tickets/ticket-list').then(m => m.TicketList) },
  { path: 'tickets/new', title: 'New ticket | Support Desk', loadComponent: () => import('./features/tickets/ticket-editor').then(m => m.TicketEditor) },
  { path: 'tickets/:id/edit', title: 'Edit ticket | Support Desk', loadComponent: () => import('./features/tickets/ticket-editor').then(m => m.TicketEditor) },
  { path: 'tickets/:id', title: 'Ticket details | Support Desk', loadComponent: () => import('./features/tickets/ticket-details').then(m => m.TicketDetails) },
  { path: '**', redirectTo: 'dashboard' },
];
