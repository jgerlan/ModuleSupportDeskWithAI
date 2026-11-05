export const statuses = ['Open', 'InProgress', 'Resolved', 'Closed'] as const;
export const priorities = ['Low', 'Medium', 'High', 'Critical'] as const;
export type TicketStatus = (typeof statuses)[number];
export type TicketPriority = (typeof priorities)[number];
export const statusLabel = (status: TicketStatus) =>
  status === 'InProgress' ? 'In progress' : status;

export interface TicketInput {
  title: string;
  description: string;
  category: string;
  status: TicketStatus;
  priority: TicketPriority;
}
export interface Ticket extends TicketInput {
  id: string;
  createdAt: string;
  updatedAt: string | null;
  resolvedAt: string | null;
}
export interface TicketQuery {
  page: number;
  pageSize: number;
  sortDirection: 'asc' | 'desc';
  search?: string;
  category?: string;
  status?: TicketStatus | '';
  priority?: TicketPriority | '';
}
export interface TicketPage {
  items: Ticket[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface TicketSummary {
  total: number;
  open: number;
  inProgress: number;
  resolved: number;
  critical: number;
}

export interface TicketAnalysis {
  suggestedCategory: string;
  suggestedPriority: TicketPriority;
  summary: string;
  possibleCauses: string[];
  recommendedActions: string[];
}
