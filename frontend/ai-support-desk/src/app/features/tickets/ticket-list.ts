import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { BehaviorSubject, catchError, debounceTime, map, merge, of, switchMap } from 'rxjs';
import { TicketApi } from './ticket-api';
import {
  priorities,
  statuses,
  statusLabel,
  TicketPage,
  TicketPriority,
  TicketQuery,
  TicketStatus,
} from './ticket-models';

@Component({
  imports: [
    DatePipe,
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTableModule,
  ],
  templateUrl: './ticket-list.html',
  styles: `
    form {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 155px), 1fr));
      gap: 12px;
    }
    .table-scroll {
      overflow-x: auto;
    }
    table {
      min-width: 630px;
    }
    .title-cell {
      overflow-wrap: anywhere;
      min-width: 140px;
    }
    .empty {
      padding: 24px 0;
      text-align: center;
    }
  `,
})
export class TicketList {
  private readonly api = inject(TicketApi);
  private readonly requests = new BehaviorSubject<TicketQuery>({
    page: 1,
    pageSize: 20,
    sortDirection: 'desc',
  });
  protected readonly query = signal(this.requests.value);
  protected readonly data = signal<TicketPage | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly columns = ['title', 'category', 'status', 'priority', 'created', 'actions'];
  protected readonly statuses = statuses;
  protected readonly priorities = priorities;
  protected readonly statusLabel = statusLabel;
  protected readonly filters = new FormGroup({
    search: new FormControl('', { nonNullable: true }),
    category: new FormControl('', { nonNullable: true }),
    status: new FormControl<TicketStatus | ''>('', { nonNullable: true }),
    priority: new FormControl<TicketPriority | ''>('', { nonNullable: true }),
    sortDirection: new FormControl<'asc' | 'desc'>('desc', { nonNullable: true }),
  });

  constructor() {
    const { search, category, status, priority, sortDirection } = this.filters.controls;
    merge(
      merge(search.valueChanges, category.valueChanges).pipe(debounceTime(300)),
      status.valueChanges,
      priority.valueChanges,
      sortDirection.valueChanges,
    )
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.refreshFilters());

    this.requests
      .pipe(
        switchMap((query) => {
          this.loading.set(true);
          this.error.set('');
          return this.api.list(query).pipe(
            map((data) => ({ data, error: '' })),
            catchError((error) => of({ data: null, error: error.message as string })),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.data.set(result.data);
        this.error.set(result.error);
        this.loading.set(false);
      });
  }

  protected retry() {
    this.requests.next(this.query());
  }

  protected changePage(event: PageEvent) {
    this.request({
      ...this.query(),
      pageSize: event.pageSize,
      page: event.pageSize === this.query().pageSize ? event.pageIndex + 1 : 1,
    });
  }

  private refreshFilters() {
    const filters = this.filters.getRawValue();
    const next = {
      ...this.query(),
      ...filters,
      page: 1,
      search: filters.search.trim(),
      category: filters.category.trim(),
    };
    if (
      Object.keys(filters).some(
        (key) =>
          next[key as keyof typeof filters] !== (this.query()[key as keyof typeof filters] ?? ''),
      )
    )
      this.request(next);
  }

  private request(query: TicketQuery) {
    this.query.set(query);
    this.requests.next(query);
  }
}
