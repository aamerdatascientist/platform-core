import type {
  CurrentUserDto,
  DynamicRow,
  FileMetadataDto,
  FormDefinitionDto,
  FormSummaryDto,
  PagedResult,
  TokenPair,
  WorkflowStatusDto,
} from '../types';
import { getTokens, setTokens } from '../auth/tokenStore';

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

// Single-flight guard: if five components all get a 401 in the same moment (e.g. right
// after the access token expires), they'd otherwise all fire their own refresh request
// simultaneously - wasteful, and the resulting rotation would invalidate refresh tokens
// out from under each other. All concurrent callers share this one in-flight promise instead.
let refreshPromise: Promise<string | null> | null = null;

async function refreshAccessToken(): Promise<string | null> {
  const stored = getTokens();
  if (!stored) return null;

  if (!refreshPromise) {
    refreshPromise = (async () => {
      try {
        const response = await fetch(`${BASE_URL}/api/auth/refresh`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken: stored.refreshToken }),
        });
        if (!response.ok) {
          // The refresh token itself is invalid/expired/revoked - no recovering from
          // this client-side. Clear everything so the app falls back to the sign-in screen.
          setTokens(null);
          return null;
        }
        const newTokens = (await response.json()) as TokenPair;
        setTokens(newTokens);
        return newTokens.accessToken;
      } finally {
        refreshPromise = null;
      }
    })();
  }
  return refreshPromise;
}

async function request<T>(path: string, options: RequestInit = {}, token?: string): Promise<T> {
  const doFetch = (accessToken?: string) => {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...(options.headers as Record<string, string> | undefined),
    };
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    return fetch(`${BASE_URL}${path}`, { ...options, headers });
  };

  let response = await doFetch(token);

  // Only attempt a silent refresh if this request was actually authenticated in the first
  // place - an anonymous 401 (e.g. wrong password on /login) should surface as a normal error.
  if (response.status === 401 && token) {
    const newAccessToken = await refreshAccessToken();
    if (newAccessToken) {
      response = await doFetch(newAccessToken);
    }
  }

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

    refresh: (refreshToken: string) =>
      request<TokenPair>('/api/auth/refresh', { method: 'POST', body: JSON.stringify({ refreshToken }) }),

    logout: (refreshToken: string) =>
      request<void>('/api/auth/logout', { method: 'POST', body: JSON.stringify({ refreshToken }) }),
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

    removeField: (token: string, formId: string, fieldId: string) =>
      request<void>(`/api/forms/${formId}/fields/${fieldId}`, { method: 'DELETE' }, token),
  },

  submissions: {
    submit: (token: string, formId: string, values: Record<string, unknown>) =>
      request<{ id: string }>(`/api/forms/${formId}/submissions`, { method: 'POST', body: JSON.stringify(values) }, token),

    list: (token: string, formId: string, page = 1, pageSize = 25) =>
      request<PagedResult<DynamicRow>>(`/api/forms/${formId}/submissions?page=${page}&pageSize=${pageSize}`, {}, token),
  },

  workflow: {
    status: (token: string, recordId: string) =>
      request<WorkflowStatusDto>(`/api/records/${recordId}/workflow`, {}, token),

    executeTransition: (token: string, recordId: string, transitionCode: string, comment?: string) =>
      request<void>(
        `/api/records/${recordId}/workflow/transitions`,
        { method: 'POST', body: JSON.stringify({ transitionCode, comment: comment ?? null }) },
        token,
      ),
  },

  files: {
    listForRecord: (token: string, recordId: string) =>
      request<FileMetadataDto[]>(`/api/records/${recordId}/attachments`, {}, token),

    getDownloadUrl: (token: string, fileId: string) =>
      request<{ url: string }>(`/api/attachments/${fileId}/download-url`, {}, token),

    delete: (token: string, fileId: string) =>
      request<void>(`/api/attachments/${fileId}`, { method: 'DELETE' }, token),

    // Can't reuse request() here - a multipart body needs the browser to set its own
    // Content-Type with the correct boundary, which request() always overrides to
    // application/json. Duplicates the 401-retry logic rather than share it, since sharing
    // would mean threading a "skip the JSON header" flag through the shared helper for a
    // single caller - not worth the added complexity for one endpoint.
    upload: async (token: string, formId: string, recordId: string, fieldCode: string, file: File): Promise<FileMetadataDto> => {
      const formData = new FormData();
      formData.append('fieldCode', fieldCode);
      formData.append('file', file);

      const doUpload = (accessToken: string) =>
        fetch(`${BASE_URL}/api/forms/${formId}/records/${recordId}/attachments`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${accessToken}` },
          body: formData,
        });

      let response = await doUpload(token);
      if (response.status === 401) {
        const newToken = await refreshAccessToken();
        if (newToken) response = await doUpload(newToken);
      }
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new ApiError(problem?.detail ?? `Upload failed with status ${response.status}`, response.status, problem?.errors);
      }
      return response.json();
    },
  },
};
