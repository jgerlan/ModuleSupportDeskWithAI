import { FormControl } from '@angular/forms';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { of, Subject } from 'rxjs';
import { TicketApi } from './ticket-api';
import { Ticket } from './ticket-models';
import { requiredText, TicketEditor } from './ticket-editor';

const ticket: Ticket = {
  id: 'ticket-1',
  title: 'Printer down',
  description: 'Paper jam',
  category: 'Hardware',
  status: 'Open',
  priority: 'High',
  createdAt: '2026-11-04T09:00:00Z',
  updatedAt: null,
  resolvedAt: null,
};

function setup(id?: string) {
  const params = convertToParamMap(id ? { id } : {});
  const response = new Subject<Ticket>();
  const api = {
    get: vi.fn(() => of(ticket)),
    create: vi.fn(() => response),
    update: vi.fn(() => response),
  };
  TestBed.configureTestingModule({
    imports: [TicketEditor],
    providers: [
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: params }, paramMap: of(params) },
      },
      { provide: TicketApi, useValue: api },
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(TicketEditor);
  fixture.detectChanges();
  const element: HTMLElement = fixture.nativeElement;
  const submit = () =>
    element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
  const field = (name: string) =>
    element.querySelector<HTMLInputElement | HTMLTextAreaElement>('#' + name)!;
  const enter = (name: string, value: string) => {
    field(name).value = value;
    field(name).dispatchEvent(new Event('input'));
  };
  return { api, response, navigate, submit, field, enter, fixture };
}

describe('TicketEditor', () => {
  it('rejects blank and oversized input using the trimmed length', () => {
    const validate = requiredText(3);
    expect(validate(new FormControl('   '))).toEqual({ required: true });
    expect(validate(new FormControl('abcd'))).toEqual({ maxlength: true });
    expect(validate(new FormControl(' abc '))).toBeNull();
  });

  it('blocks an invalid create and prevents a duplicate while saving', () => {
    const { api, response, navigate, submit, enter } = setup();
    submit();
    expect(api.create).not.toHaveBeenCalled();
    enter('title', ticket.title);
    enter('description', ticket.description);
    enter('category', ticket.category);
    submit();
    submit();
    expect(api.create).toHaveBeenCalledTimes(1);
    expect(api.create).toHaveBeenCalledWith(
      expect.objectContaining({ title: ticket.title, status: 'Open' }),
    );
    response.next(ticket);
    response.complete();
    expect(navigate).toHaveBeenCalledWith(['/tickets', ticket.id]);
  });

  it('loads an existing ticket and updates its editable fields', () => {
    const { api, submit, field, enter } = setup(ticket.id);
    expect(api.get).toHaveBeenCalledWith(ticket.id);
    expect(field('title').value).toBe(ticket.title);
    enter('title', 'Printer working');
    submit();
    expect(api.update).toHaveBeenCalledWith(ticket.id, {
      title: 'Printer working',
      description: ticket.description,
      category: ticket.category,
      status: ticket.status,
      priority: ticket.priority,
    });
    expect(api.create).not.toHaveBeenCalled();
  });
});
