import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

const analysisMessages: Record<number, string> = {
  502: 'O serviço de IA retornou uma análise inválida. Tente novamente.',
  503: 'O serviço de IA está indisponível. Verifique o provedor e o modelo configurados.',
  504: 'A análise excedeu o tempo limite. Tente novamente.',
};

function errorMessage(error: HttpErrorResponse, analysisRequest: boolean): string {
  if (error.status === 0) {
    return 'Unable to reach the server. Try again.';
  }

  if (analysisRequest) {
    if (error.status === 503 && error.error?.code === 'analysis_authentication_failed') {
      return 'A API não conseguiu autenticar no LiteLLM. Verifique LiteLLM:ApiKey e reinicie o backend.';
    }

    if (analysisMessages[error.status]) {
      return analysisMessages[error.status];
    }
  }

  if (error.status >= 500) {
    return 'The server could not complete this request. Try again.';
  }

  return error.error?.title ?? 'The request could not be completed.';
}

export const apiErrors: HttpInterceptorFn = (request, next) =>
  next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      const analysisRequest = /\/api\/tickets\/[^/?]+\/analysis(?:\?.*)?$/.test(request.url);
      const message = errorMessage(error, analysisRequest);
      return throwError(() => new ApiError(message, error.status, error.error?.errors ?? {}));
    }),
  );
