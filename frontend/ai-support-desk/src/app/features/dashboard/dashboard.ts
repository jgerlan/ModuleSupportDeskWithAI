import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TicketApi } from '../tickets/ticket-api';
import { TicketSummary } from '../tickets/ticket-models';

@Component({
  imports: [RouterLink, MatButtonModule, MatProgressBarModule],
  template: `
    <div class="page-heading">
      <div>
        <h1>Dashboard</h1>
        <p class="muted">Monitor your support queue at a glance.</p>
      </div>
      <a mat-flat-button routerLink="/tickets/new">New ticket</a>
    </div>
    @if (summary(); as data) {
      <section class="cards" aria-label="Ticket counts">
        @for (metric of metrics; track metric.key) {
          <div class="surface">
            <h2>{{ metric.label }}</h2>
            <strong>{{ data[metric.key] }}</strong>
          </div>
        }
      </section>
      <p class="browse"><a mat-stroked-button routerLink="/tickets">Browse tickets</a></p>
    } @else if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    } @else {
      <mat-progress-bar mode="indeterminate" aria-label="Loading ticket counts" />
    }
  `,
  styles: `
    .cards {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
      gap: 14px;
    }
    strong {
      font-size: 2rem;
    }
    .browse {
      margin-top: 24px;
    }
  `,
})
export class Dashboard {
  protected readonly summary = signal<TicketSummary | null>(null);
  protected readonly error = signal('');
  protected readonly metrics: { key: keyof TicketSummary; label: string }[] = [
    { key: 'total', label: 'Total tickets' },
    { key: 'open', label: 'Open' },
    { key: 'inProgress', label: 'In progress' },
    { key: 'resolved', label: 'Resolved' },
    { key: 'critical', label: 'Critical' },
  ];

  constructor() {
    inject(TicketApi)
      .summary()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (summary) => this.summary.set(summary),
        error: (error) => this.error.set(error.message),
      });
  }
}
