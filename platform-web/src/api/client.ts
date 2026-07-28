import type {
  CurrentUserDto,
  DynamicRow,
  FormDefinitionDto,
  FormSummaryDto,
  PagedResult,
  TokenPair,
} from '../types';

// Set VITE_API_BASE_URL in a .env.local file once you know where the API is actually
// reachable (localhost while developing, or wherever it ends up deployed).
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080';

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public errors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function request<T>(path: string, options: RequestInit = {}, token?: string): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  };
  if (token) headers.Authorization = `Bearer ${token}`;

  const response = await fetch(`${BASE_URL}${path}`, { ...options, headers });

  if (!response.ok) {
    // Matches Platform.Api.Middleware.ExceptionHandlingMiddleware's problem+json shape.
    const problem = await response.json().catch(() => null);
    throw new ApiError(problem?.detail ?? `Request failed with status ${response.status}`, response.status, problem?.errors);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const api = {
  auth: {
    login: (email: string, password: string) =>
      request<TokenPair>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),

    register: (input: { email: string; displayName: string; password: string; departmentId?: string | null; defaultRoleId: string }) =>
      request<{ id: string }>('/api/auth/register', { method: 'POST', body: JSON.stringify(input) }),

    me: (token: string) => request<CurrentUserDto>('/api/auth/me', {}, token),
  },

  forms: {
    list: (token: string, moduleName?: string) =>
      request<FormSummaryDto[]>(`/api/forms${moduleName ? `?moduleName=${encodeURIComponent(moduleName)}` : ''}`, {}, token),

    create: (token: string, input: { code: string; name: string; moduleName: string; description?: string | null }) =>
      request<{ id: string }>('/api/forms', { method: 'POST', body: JSON.stringify(input) }, token),

    get: (token: string, formId: string) => request<FormDefinitionDto>(`/api/forms/${formId}`, {}, token),

    addField: (
      token: string,
      formId: string,
      input: {
        code: string;
        label: string;
        fieldType: string;
        isRequired: boolean;
        optionsJson?: string | null;
        lookupFormDefinitionId?: string | null;
        validationRulesJson?: string | null;
      },
    ) => request<{ id: string }>(`/api/forms/${formId}/fields`, { method: 'POST', body: JSON.stringify(input) }, token),

    publish: (token: string, formId: string) =>
      request<{ formVersionId: string; versionNumber: number; tableName: string }>(
        `/api/forms/${formId}/publish`,
        { method: 'POST' },
        token,
      ),
  },

  submissions: {
    submit: (token: string, formId: string, values: Record<string, unknown>) =>
      request<{ id: string }>(`/api/forms/${formId}/submissions`, { method: 'POST', body: JSON.stringify(values) }, token),

    list: (token: string, formId: string, page = 1, pageSize = 25) =>
      request<PagedResult<DynamicRow>>(`/api/forms/${formId}/submissions?page=${page}&pageSize=${pageSize}`, {}, token),
  },
};
