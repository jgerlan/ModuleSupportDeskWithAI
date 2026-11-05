import { Component, input } from '@angular/core';
import { TicketAnalysis } from './ticket-models';

@Component({
  selector: 'app-ticket-analysis',
  template: `
    <section class="surface" aria-label="Análise de IA">
      <h2>Sugestões geradas por IA</h2>
      <p class="muted">Revise as sugestões antes de tomar decisões.</p>
      <dl>
        <dt>Categoria sugerida</dt>
        <dd>{{ analysis().suggestedCategory }}</dd>
        <dt>Prioridade sugerida</dt>
        <dd>
          <span class="badge" [attr.data-value]="analysis().suggestedPriority">{{
            analysis().suggestedPriority
          }}</span>
        </dd>
      </dl>
      <h3>Resumo</h3>
      <p>{{ analysis().summary }}</p>
      <h3>Possíveis causas</h3>
      <ul>
        @for (cause of analysis().possibleCauses; track $index) {
          <li>{{ cause }}</li>
        }
      </ul>
      @if (!analysis().possibleCauses.length) {
        <p>Informação insuficiente para sugerir causas.</p>
      }
      <h3>Ações recomendadas</h3>
      <ol>
        @for (action of analysis().recommendedActions; track $index) {
          <li>{{ action }}</li>
        }
      </ol>
      @if (!analysis().recommendedActions.length) {
        <p>Informação insuficiente para sugerir ações.</p>
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
      margin-top: 20px;
    }
    section {
      overflow-wrap: anywhere;
    }
    dt {
      font-weight: 600;
    }
    dd {
      margin: 4px 0 12px;
    }
  `,
})
export class TicketAnalysisComponent {
  readonly analysis = input.required<TicketAnalysis>();
}
