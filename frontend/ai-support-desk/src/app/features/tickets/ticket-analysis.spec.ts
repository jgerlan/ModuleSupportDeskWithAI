import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of, Subject } from 'rxjs';
import { apiErrors, ApiError } from '../../core/api-errors';
import { TicketApi } from './ticket-api';
import { Ticket } from './ticket-models';
import { TicketAnalysisComponent } from './ticket-analysis';
import { TicketAnalysis } from './ticket-models';
import { TicketDetails } from './ticket-details';

const analysis: TicketAnalysis = {
  suggestedCategory: 'Aplicações',
  suggestedPriority: 'High',
  summary: 'Resumo do problema',
  possibleCauses: ['Hipótese de falha'],
  recommendedActions: ['Verificar os logs'],
};
const ticket: Ticket = {
  id: 'ticket-a',
  title: 'Title',
  description: 'Description',
  category: 'Category',
  status: 'Open',
  priority: 'Medium',
  createdAt: '2026-11-05T09:00:00Z',
  updatedAt: null,
  resolvedAt: null,
};

describe('Analysis HTTP integration', () => {
  let http: HttpTestingController;
  let api: TicketApi;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiErrors])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(TicketApi);
  });
  afterEach(() => http.verify());

  it('sends only the ticket ID in a POST path', () => {
    let result: TicketAnalysis | undefined;
    api.analyze('ticket-a').subscribe((value) => (result = value));
    const request = http.expectOne({ method: 'POST', url: '/api/tickets/ticket-a/analysis' });
    expect(request.request.body).toBeNull();
    request.flush(analysis);
    expect(result).toEqual(analysis);
  });

  it.each([502, 503, 504])('shows a safe analysis-specific message for %i', (status) => {
    let failure: ApiError | undefined;
    api.analyze('ticket-a').subscribe({ error: (error) => (failure = error) });
    http
      .expectOne('/api/tickets/ticket-a/analysis')
      .flush({ title: 'private backend details' }, { status, statusText: 'Error' });
    expect(failure?.status).toBe(status);
    expect(failure?.message).not.toContain('private');
    expect(failure?.message).not.toContain('The server could not');
  });

  it('distinguishes proxy authentication failure from service unavailability', () => {
    let message = '';
    api.analyze('ticket-a').subscribe({ error: (error) => (message = error.message) });
    http
      .expectOne('/api/tickets/ticket-a/analysis')
      .flush(
        { code: 'analysis_authentication_failed', title: 'private proxy details' },
        { status: 503, statusText: 'Unavailable' },
      );
    expect(message).toContain('LiteLLM:ApiKey');
    expect(message).not.toContain('private');
  });
  it('preserves generic error behavior for CRUD', () => {
    let message = '';
    api.get('ticket-a').subscribe({ error: (error) => (message = error.message) });
    http
      .expectOne('/api/tickets/ticket-a')
      .flush({ title: 'private' }, { status: 503, statusText: 'Unavailable' });
    expect(message).toBe('The server could not complete this request. Try again.');
  });
});

describe('Analysis presentation', () => {
  it('renders suggestions as text', () => {
    const fixture = TestBed.createComponent(TicketAnalysisComponent);
    fixture.componentRef.setInput('analysis', {
      ...analysis,
      summary: '<img src=x onerror=alert(1)>',
    });
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('Sugestões geradas por IA');
    expect(element.textContent).toContain('Aplicações');
    expect(element.textContent).toContain('High');
    expect(element.textContent).toContain('Hipótese de falha');
    expect(element.textContent).toContain('Verificar os logs');
    expect(element.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(element.querySelector('img')).toBeNull();
  });
});

describe('TicketDetails analysis lifecycle', () => {
  function setup() {
    const route = new BehaviorSubject(convertToParamMap({ id: ticket.id }));
    const pending: Subject<TicketAnalysis>[] = [];
    const analyze = vi.fn(() => {
      const response = new Subject<TicketAnalysis>();
      pending.push(response);
      return response.asObservable();
    });
    const get = vi.fn((id: string) => of({ ...ticket, id }));
    TestBed.configureTestingModule({
      imports: [TicketDetails],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: route } },
        { provide: TicketApi, useValue: { analyze, get } },
      ],
    });
    const fixture = TestBed.createComponent(TicketDetails);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    const button = () =>
      Array.from(element.querySelectorAll<HTMLButtonElement>('button')).find((item) =>
        /Analyze with AI|Analisando/.test(item.textContent ?? ''),
      )!;
    return { fixture, element, button, analyze, pending, route };
  }

  it('blocks duplicates, displays an error, and supports another attempt', () => {
    const { fixture, element, button, analyze, pending } = setup();
    button().click();
    button().click();
    fixture.detectChanges();
    expect(analyze).toHaveBeenCalledTimes(1);
    expect(analyze).toHaveBeenCalledWith(ticket.id);
    expect(button().disabled).toBe(true);
    pending[0].error(new Error('O serviço de IA está indisponível.'));
    fixture.detectChanges();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('indisponível');
    expect(button().disabled).toBe(false);
    button().click();
    pending[1].next(analysis);
    pending[1].complete();
    fixture.detectChanges();
    expect(element.textContent).toContain(analysis.summary);
    expect(element.querySelector('[role="alert"]')).toBeNull();
  });

  it('cancels requests on navigation and destruction and clears earlier results', () => {
    const { fixture, element, button, pending, route } = setup();
    button().click();
    expect(pending[0].observed).toBe(true);
    route.next(convertToParamMap({ id: 'ticket-b' }));
    expect(pending[0].observed).toBe(false);
    pending[0].next(analysis);
    fixture.detectChanges();
    expect(element.querySelector('app-ticket-analysis')).toBeNull();

    button().click();
    pending[1].next(analysis);
    pending[1].complete();
    fixture.detectChanges();
    expect(element.textContent).toContain(analysis.summary);
    route.next(convertToParamMap({ id: 'ticket-c' }));
    fixture.detectChanges();
    expect(element.querySelector('app-ticket-analysis')).toBeNull();
    button().click();
    expect(pending[2].observed).toBe(true);
    fixture.destroy();
    expect(pending[2].observed).toBe(false);
  });
});
