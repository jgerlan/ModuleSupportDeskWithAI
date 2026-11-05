import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { apiErrors, ApiError } from '../../core/api-errors';
import { TicketApi } from './ticket-api';

describe('TicketApi', () => {
  let api: TicketApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([apiErrors])), provideHttpClientTesting()],
    });
    api = TestBed.inject(TicketApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends pagination and literal search while omitting empty filters', () => {
    api
      .list({ page: 2, pageSize: 20, sortDirection: 'asc', search: '%_&', status: '' })
      .subscribe();
    const request = http.expectOne((req) => req.url === '/api/tickets');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('sortDirection')).toBe('asc');
    expect(request.request.params.get('search')).toBe('%_&');
    expect(request.request.params.has('status')).toBe(false);
    request.flush({ items: [], page: 2, pageSize: 20, totalCount: 0 });
  });

  it('preserves validation messages from Problem Details', () => {
    let failure: ApiError | undefined;
    const input = {
      title: ' ',
      description: 'Details',
      category: 'Hardware',
      status: 'Open',
      priority: 'High',
    } as const;
    api.create(input).subscribe({ error: (error) => (failure = error) });
    const request = http.expectOne({ url: '/api/tickets', method: 'POST' });
    expect(request.request.body).toEqual(input);
    request.flush(
      { title: 'Validation failed', errors: { title: ['Required'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    expect(failure?.fields['title']).toEqual(['Required']);
  });
});
