import { DatePipe } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, filter, finalize, of, Subscription, switchMap, tap } from 'rxjs';
import { TicketAnalysisComponent } from './ticket-analysis';
import { TicketAnalysis } from './ticket-models';
import { TicketApi } from './ticket-api';
import { statusLabel, Ticket } from './ticket-models';

@Component({
  imports: [MatButtonModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>Delete ticket?</h2>
    <mat-dialog-content>“{{ title }}” will be permanently deleted.</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false" cdkFocusInitial>Cancel</button>
      <button mat-flat-button [mat-dialog-close]="true">Delete ticket</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmTicketDeletion {
  protected readonly title: string = inject(MAT_DIALOG_DATA);
}

@Component({
  imports: [TicketAnalysisComponent, DatePipe, RouterLink, MatButtonModule, MatProgressBarModule],
  templateUrl: './ticket-details.html',
  styles: `
    .description {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
    dl {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 190px), 1fr));
      gap: 16px;
    }
    dt {
      font-weight: 600;
    }
    dd {
      margin: 4px 0;
      overflow-wrap: anywhere;
    }
  `,
})
export class TicketDetails {
  private readonly api = inject(TicketApi);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly ticket = signal<Ticket | null>(null);
  protected readonly error = signal('');
  protected readonly loading = signal(true);
  protected readonly deleting = signal(false);
  protected readonly statusLabel = statusLabel;

  private analysisRequest?: Subscription;
  protected readonly analysis = signal<TicketAnalysis | null>(null);
  protected readonly analyzing = signal(false);
  protected readonly analysisError = signal('');

  protected analyze() {
    const ticket = this.ticket();
    if (!ticket || this.analyzing() || this.deleting()) return;
    this.analysis.set(null);
    this.analysisError.set('');
    this.analyzing.set(true);
    this.analysisRequest = this.api
      .analyze(ticket.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.analyzing.set(false)),
      )
      .subscribe({
        next: (analysis) => this.analysis.set(analysis),
        error: (error) => this.analysisError.set(error.message),
      });
  }

  constructor() {
    inject(ActivatedRoute)
      .paramMap.pipe(
        switchMap((params) => {
          this.analysisRequest?.unsubscribe();
          this.analysis.set(null);
          this.analysisError.set('');
          this.loading.set(true);
          this.ticket.set(null);
          this.error.set('');
          return this.api.get(params.get('id')!).pipe(
            catchError((error) => {
              this.error.set(error.message);
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((ticket) => {
        this.ticket.set(ticket);
        this.loading.set(false);
      });
  }

  protected confirmDelete(ticket: Ticket) {
    this.dialog
      .open<ConfirmTicketDeletion, string, boolean>(ConfirmTicketDeletion, { data: ticket.title })
      .afterClosed()
      .pipe(
        filter((confirmed) => confirmed === true),
        tap(() => {
          this.error.set('');
          this.deleting.set(true);
        }),
        switchMap(() => this.api.delete(ticket.id)),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.deleting.set(false)),
      )
      .subscribe({
        next: () => {
          void this.router.navigate(['/tickets']);
        },
        error: (error) => this.error.set(error.message),
      });
  }
}
