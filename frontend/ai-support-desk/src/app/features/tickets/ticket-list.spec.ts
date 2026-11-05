import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { TicketApi } from './ticket-api';
import { TicketPage, TicketQuery } from './ticket-models';
import { TicketList } from './ticket-list';

describe('TicketList', () => {
  it('renders failures, retries, paginates, and resets the page on debounced search', async () => {
    const list = vi.fn((query: TicketQuery) =>
      of<TicketPage>({
        items: [],
        page: query.page,
        pageSize: query.pageSize,
        totalCount: 41,
      }),
    );
    list.mockReturnValueOnce(throwError(() => new Error('Server unavailable')));
    TestBed.configureTestingModule({
      imports: [TicketList],
      providers: [provideRouter([]), { provide: TicketApi, useValue: { list } }],
    });
    const fixture = TestBed.createComponent(TicketList);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Server unavailable');
    element.querySelector<HTMLButtonElement>('button')!.click();
    fixture.detectChanges();
    expect(element.textContent).toContain('No tickets match');

    element.querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();
    expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }));
    const search = element.querySelector<HTMLInputElement>('input[formcontrolname="search"]')!;
    search.value = ' printer ';
    search.dispatchEvent(new Event('input'));
    expect(list).toHaveBeenCalledTimes(3);
    await vi.waitFor(() =>
      expect(list).toHaveBeenLastCalledWith(
        expect.objectContaining({ page: 1, search: 'printer' }),
      ),
    );
  });
});
