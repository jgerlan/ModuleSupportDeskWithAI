import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('Support Desk shell', () => {
  it('links to the dashboard and ticket workspace', () => {
    TestBed.configureTestingModule({ imports: [App], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('nav').textContent).toContain('Dashboard');
    expect(fixture.nativeElement.querySelector('nav').textContent).toContain('Tickets');
  });
});
