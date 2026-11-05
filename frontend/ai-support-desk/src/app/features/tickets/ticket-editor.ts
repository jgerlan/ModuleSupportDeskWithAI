import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, finalize, of, switchMap } from 'rxjs';
import { ApiError } from '../../core/api-errors';
import { TicketApi } from './ticket-api';
import {
  priorities,
  statuses,
  statusLabel,
  TicketInput,
  TicketPriority,
  TicketStatus,
} from './ticket-models';

export function requiredText(maxLength: number) {
  return (control: AbstractControl) => {
    const text = String(control.value ?? '').trim();
    if (!text) return { required: true };
    return text.length <= maxLength ? null : { maxlength: true };
  };
}

@Component({
  imports: [
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
  ],
  templateUrl: './ticket-editor.html',
  styles: `
    form {
      display: grid;
      gap: 8px;
      max-width: 760px;
    }
    .row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 180px), 1fr));
      gap: 12px;
    }
  `,
})
export class TicketEditor {
  private readonly api = inject(TicketApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly id = signal<string | null>(this.route.snapshot.paramMap.get('id'));
  protected readonly loading = signal(!!this.id());
  protected readonly saving = signal(false);
  protected readonly loadError = signal('');
  protected readonly submitError = signal('');
  protected readonly statuses = statuses;
  protected readonly priorities = priorities;
  protected readonly statusLabel = statusLabel;
  protected readonly cancelLink = computed(() =>
    this.id() ? ['/tickets', this.id()] : ['/tickets'],
  );
  protected readonly fields = [
    { key: 'title', label: 'Title', limit: 150 },
    { key: 'description', label: 'Description', limit: 5000 },
    { key: 'category', label: 'Category', limit: 100 },
  ] as const;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    title: ['', requiredText(150)],
    description: ['', requiredText(5000)],
    category: ['', requiredText(100)],
    status: ['Open' as TicketStatus],
    priority: ['Medium' as TicketPriority],
  });

  constructor() {
    this.route.paramMap
      .pipe(
        switchMap((params) => {
          const id = params.get('id');
          this.id.set(id);
          this.loadError.set('');
          this.submitError.set('');
          this.form.reset();
          this.loading.set(!!id);
          return id
            ? this.api.get(id).pipe(
                catchError((error) => {
                  this.loadError.set(error.message);
                  return of(null);
                }),
              )
            : of(null);
        }),
        takeUntilDestroyed(),
      )
      .subscribe({
        next: (ticket) => {
          if (ticket) this.form.patchValue(ticket);
          this.loading.set(false);
        },
      });
  }

  protected save() {
    if (this.saving() || this.loading() || this.loadError()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const input: TicketInput = this.form.getRawValue();
    const id = this.id();
    this.saving.set(true);
    this.submitError.set('');
    this.form.disable();
    const request = id ? this.api.update(id, input) : this.api.create(input);
    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          if (this.form.disabled) this.form.enable();
          this.saving.set(false);
        }),
      )
      .subscribe({
        next: (ticket) => {
          void this.router.navigate(['/tickets', ticket.id]);
        },
        error: (error: ApiError) => {
          this.submitError.set(error.message);
          this.form.enable();
          for (const key of Object.keys(this.form.controls) as (keyof TicketInput)[]) {
            const messages = error.fields[key] ?? error.fields['$.' + key];
            if (messages?.length) {
              this.form.controls[key].setErrors({ server: messages[0] });
              this.form.controls[key].markAsTouched();
            }
          }
        },
      });
  }

  protected fieldError(field: keyof TicketInput, label: string, limit?: number) {
    const errors = this.form.controls[field].errors;
    if (errors?.['server']) return errors['server'];
    if (errors?.['maxlength']) return `Use ${limit} characters or fewer.`;
    return `${label} is required.`;
  }
}
