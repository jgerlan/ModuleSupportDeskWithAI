import {
  Ticket,
  TicketAnalysis,
  TicketInput,
  TicketPage,
  TicketQuery,
  TicketSummary,
} from './ticket-models';
import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../core/environment';

@Injectable({ providedIn: 'root' })
export class TicketApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/tickets';

  list(query: TicketQuery) {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== '' && value !== undefined) params = params.set(key, value);
    }
    return this.http.get<TicketPage>(this.url, { params });
  }

  analyze(id: string) {
    return this.http.post<TicketAnalysis>(
      this.url + '/' + encodeURIComponent(id) + '/analysis',
      null,
    );
  }
  summary() {
    return this.http.get<TicketSummary>(this.url + '/summary');
  }
  get(id: string) {
    return this.http.get<Ticket>(this.url + '/' + encodeURIComponent(id));
  }
  create(input: TicketInput) {
    return this.http.post<Ticket>(this.url, input);
  }
  update(id: string, input: TicketInput) {
    return this.http.put<Ticket>(this.url + '/' + encodeURIComponent(id), input);
  }
  delete(id: string) {
    return this.http.delete<void>(this.url + '/' + encodeURIComponent(id));
  }
}
