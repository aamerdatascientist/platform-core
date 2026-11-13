import type {
  ChoiceOptionDto,
  FormSectionDto,
  ProjectProgressDto,
  MyEntriesPageDto,
  RecordDetailDto,
  RecordQuery,
  RecordsPageDto,
  FormStepDto,
  AssignmentDayDto,
  CurrentUserDto,
  FormAssignmentDto,
  MyAssignmentsDto,
  DynamicRow,
  ExecutiveOverviewDto,
  FileMetadataDto,
  FormDefinitionDto,
  FormSummaryDto,
  PagedResult,
  ReportKind,
  ReportRecipientDto,
  ReportRunDto,
  MessagingScheduleDto,
  ReportsOverviewDto,
  RoleDto,
  TelegramLinkDto,
  TelegramStatusDto,
  TokenPair,
  UserSummaryDto,
  WorkflowStatusDto,
} from '../types';
import { endSession, getTokens, setTokens } from '../auth/tokenStore';
import { shrinkImageForUpload } from '../forms/shrinkImage';

// Set VITE_API_BASE_URL in a .env.local file once you know where the API is actually
// reachable (localhost while developing, or wherever it ends up deployed).
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080';

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public errors?: Record<string, string[]>,
    // Stable, translatable identity for this specific error (e.g.
    // "form.field.codeMustBeLatin") - see Platform.Application.Common.Exceptions.ValidationException.Code
    // on the backend. Undefined for errors that don't have one yet; callers fall back to
    // `message` (the backend's English text) in that case - see api/errorMessage.ts.
    public code?: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

const UPLOAD_TYPES = new Set(['image/jpeg', 'image/png', 'image/heic', 'image/webp', 'application/pdf']);
const UPLOAD_MAX_BYTES = 20 * 1024 * 1024;

// The access token expired and renewing it failed for a reason other than the server
// refusing it (it was restarting, say): the person is still signed in, so this reads as
// "busy, try again", not as a sign-in problem.
function serverBusyError(): ApiError {
  return new ApiError('The server could not renew the sign-in right now.', 503);
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
        const response = await fetchOnceMore(
          `${BASE_URL}/api/auth/refresh`,
          { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: stored.refreshToken }) },
          true,
        );
        if (!response.ok) {
          // Another tab may have refreshed first: refresh tokens are single-use, so ours was
          // already spent, but the new pair is in shared storage - use it instead of signing
          // everyone out.
          const latest = getTokens();
          if (latest && latest.refreshToken !== stored.refreshToken) return latest.accessToken;
          // Only the server refusing the refresh token (expired or revoked: 401/403) ends the
          // session. Any other failure - a 5xx while the API restarts during a deploy - keeps
          // the sign-in, so the next request can renew it; this used to sign people out.
          if (response.status === 401 || response.status === 403) endSession();
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

/**
 * Requests that can safely go twice: reading, and signing in / renewing the session.
 * iPhone Safari reuses a connection the server has already closed after a few idle minutes,
 * and the first request then fails at once ("network connection lost") without reaching the
 * server - the "couldn't reach the server" on the sign-in page (2026-11-07). These get one
 * quiet retry on a fresh connection; anything that writes data never does (it could arrive
 * twice - the offline queue's idempotency key handles that for entries).
 */
function retriable(path: string, method: string) {
  return method === 'GET' || path === '/api/auth/login' || path === '/api/auth/refresh';
}

async function fetchOnceMore(url: string, init: RequestInit, retry: boolean) {
  try {
    return await fetch(url, init);
  } catch (err) {
    if (!retry || !(err instanceof TypeError)) throw err;
    await new Promise((resolve) => setTimeout(resolve, 400));
    return fetch(url, init);
  }
}

async function request<T>(path: string, options: RequestInit = {}, token?: string): Promise<T> {
  const doFetch = (accessToken?: string) => {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...(options.headers as Record<string, string> | undefined),
    };
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    return fetchOnceMore(`${BASE_URL}${path}`, { ...options, headers }, retriable(path, (options.method ?? 'GET').toUpperCase()));
  };

  let response = await doFetch(token);

  // Only attempt a silent refresh if this request was actually authenticated in the first
  // place - an anonymous 401 (e.g. wrong password on /login) should surface as a normal error.
  if (response.status === 401 && token) {
    const newAccessToken = await refreshAccessToken();
    if (newAccessToken) {
      response = await doFetch(newAccessToken);
    } else if (getTokens()) {
      throw serverBusyError();
    }
  }

  if (!response.ok) {
    // Matches Platform.Api.Middleware.ExceptionHandlingMiddleware's problem+json shape.
    const problem = await response.json().catch(() => null);
    throw new ApiError(
      problem?.detail ?? `Request failed with status ${response.status}`,
      response.status,
      problem?.errors,
      problem?.code,
    );
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

/** "?projectId=...&phase=..." for the dashboard, or "" when neither is set. */
function overviewQuery(projectId?: string | null, phase?: string | null) {
  const params = new URLSearchParams();
  if (projectId) params.set('projectId', projectId);
  if (phase) params.set('phase', phase);
  const query = params.toString();
  return query ? `?${query}` : '';
}

export const api = {
  auth: {
    login: (email: string, password: string) =>
      request<TokenPair>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),

    register: (token: string, input: { email: string; displayName: string; password: string; departmentId?: string | null; defaultRoleId: string }) =>
      request<{ id: string }>('/api/auth/register', { method: 'POST', body: JSON.stringify(input) }, token),

    me: (token: string) => request<CurrentUserDto>('/api/auth/me', {}, token),

    refresh: (refreshToken: string) =>
      request<TokenPair>('/api/auth/refresh', { method: 'POST', body: JSON.stringify({ refreshToken }) }),

    logout: (refreshToken: string) =>
      request<void>('/api/auth/logout', { method: 'POST', body: JSON.stringify({ refreshToken }) }),

    /** Returns a fresh token pair for this device; every other session is signed out. */
    changePassword: (token: string, currentPassword: string, newPassword: string) =>
      request<TokenPair>(
        '/api/auth/change-password',
        { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) },
        token,
      ),
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
        visibleWhenFieldCode?: string | null;
        visibleWhenValuesJson?: string | null;
        filterByFieldCode?: string | null;
        dynamicOptionsSourceFormDefinitionId?: string | null;
        dynamicOptionsSourceFieldCode?: string | null;
        /** English label and step (redesign §9.1/§9.2). */
        labelEn?: string | null;
        stepId?: string | null;
        /** Repeatable section and the settings added with stage progress (2026-11-10). */
        sectionId?: string | null;
        optionsProvider?: string | null;
        formula?: string | null;
        rowFilterJson?: string | null;
        visibleWhenAttribute?: string | null;
      },
    ) => request<{ id: string }>(`/api/forms/${formId}/fields`, { method: 'POST', body: JSON.stringify(input) }, token),

    publish: (token: string, formId: string) =>
      request<{ formVersionId: string; versionNumber: number; tableName: string }>(
        `/api/forms/${formId}/publish`,
        { method: 'POST' },
        token,
      ),

    // --- Safe field edits: metadata only, no confirmation needed even on a published form ---

    /** labelEn: undefined leaves the English label as it is, '' clears it. */
    updateFieldLabel: (token: string, formId: string, fieldId: string, label: string, labelEn?: string) =>
      request<void>(
        `/api/forms/${formId}/fields/${fieldId}/label`,
        { method: 'PUT', body: JSON.stringify({ label, labelEn: labelEn ?? null }) },
        token,
      ),

    // --- Redesign §9.1/§9.2: names in both languages and filling steps (all Safe) ---

    updateName: (token: string, formId: string, name: string, nameEn: string) =>
      request<void>(`/api/forms/${formId}/name`, { method: 'PUT', body: JSON.stringify({ name, nameEn }) }, token),

    /** Relabel (and reorder) a static dropdown's options; the set of values must stay the same. */
    updateOptionLabels: (token: string, formId: string, fieldId: string, options: { value: string; label: string; labelEn: string | null }[]) =>
      request<void>(`/api/forms/${formId}/fields/${fieldId}/option-labels`, { method: 'PUT', body: JSON.stringify({ options }) }, token),

    addStep: (token: string, formId: string, titleAr: string, titleEn: string) =>
      request<FormStepDto>(`/api/forms/${formId}/steps`, { method: 'POST', body: JSON.stringify({ titleAr, titleEn }) }, token),

    updateStep: (token: string, formId: string, stepId: string, titleAr: string, titleEn: string) =>
      request<void>(`/api/forms/${formId}/steps/${stepId}`, { method: 'PUT', body: JSON.stringify({ titleAr, titleEn }) }, token),

    reorderSteps: (token: string, formId: string, stepIds: string[]) =>
      request<void>(`/api/forms/${formId}/steps/order`, { method: 'PUT', body: JSON.stringify({ stepIds }) }, token),

    /** Its fields move to the previous step (the next one when it was first). */
    deleteStep: (token: string, formId: string, stepId: string) =>
      request<void>(`/api/forms/${formId}/steps/${stepId}`, { method: 'DELETE' }, token),

    /** null = the first step. */
    setFieldStep: (token: string, formId: string, fieldId: string, stepId: string | null) =>
      request<void>(`/api/forms/${formId}/fields/${fieldId}/step`, { method: 'PUT', body: JSON.stringify({ stepId }) }, token),

    /** Pass both as null/undefined to clear the condition (always visible again). */
    updateFieldVisibility: (
      token: string,
      formId: string,
      fieldId: string,
      input: { visibleWhenFieldCode?: string | null; visibleWhenValuesJson?: string | null },
    ) =>
      request<void>(
        `/api/forms/${formId}/fields/${fieldId}/visibility`,
        { method: 'PUT', body: JSON.stringify(input) },
        token,
      ),

    /** Whether the field must be answered, and its value rules. Works on a live form;
     *  enforced for new submissions and record edits. */
    updateFieldRules: (
      token: string,
      formId: string,
      fieldId: string,
      input: {
        isRequired: boolean;
        min: number | null;
        max: number | null;
        minExclusive: boolean;
        regex: string | null;
        noFutureDates: boolean;
        unique: boolean;
        uniqueWithin: string | null;
      },
    ) =>
      request<void>(
        `/api/forms/${formId}/fields/${fieldId}/rules`,
        { method: 'PUT', body: JSON.stringify(input) },
        token,
      ),

    /** Pass null/undefined to clear the filter (shows every target-form row again). Only
     *  meaningful on a Lookup field. */
    updateFieldLookupFilter: (token: string, formId: string, fieldId: string, filterByFieldCode?: string | null) =>
      request<void>(
        `/api/forms/${formId}/fields/${fieldId}/lookup-filter`,
        { method: 'PUT', body: JSON.stringify({ filterByFieldCode }) },
        token,
      ),

    /** Pass both as null/undefined to clear the source (back to this Dropdown's own static
     *  OptionsJson). Only meaningful on a Dropdown field. */
    updateFieldDynamicOptionsSource: (
      token: string,
      formId: string,
      fieldId: string,
      input: { sourceFormDefinitionId?: string | null; sourceFieldCode?: string | null },
    ) =>
      request<void>(
        `/api/forms/${formId}/fields/${fieldId}/dynamic-options-source`,
        { method: 'PUT', body: JSON.stringify(input) },
        token,
      ),

    /** The live option list for a Dropdown field sourced dynamically from another form's
     *  field - distinct, non-blank values currently submitted there. Empty for a field with
     *  no dynamic source configured. */
    getFieldDynamicOptions: (token: string, formId: string, fieldId: string) =>
      request<string[]>(`/api/forms/${formId}/fields/${fieldId}/dynamic-options`, {}, token),

    /**
     * The choices for a Lookup field on this form: each target row's id and display label,
     * nothing else. Checked against access to THIS form, so it works for people who can't
     * open the target form itself. filterValue: the sibling value a cascading Lookup is
     * narrowed by (e.g. the picked project, for Zone).
     */
    lookupOptions: (token: string, formId: string, fieldId: string, filterValue?: string, values?: Record<string, string>) => {
      // values: the form's current values a provider needs (e.g. project and stage of a block).
      const query = new URLSearchParams();
      if (filterValue) query.set('filterValue', filterValue);
      Object.entries(values ?? {}).forEach(([code, value]) => {
        if (value) query.append('v', `${code}:${value}`);
      });
      const qs = query.toString();
      return request<ChoiceOptionDto[]>(`/api/forms/${formId}/fields/${fieldId}/lookup-options${qs ? `?${qs}` : ''}`, {}, token);
    },

    // Repeatable sections and the field settings added with stage progress (2026-11-10).
    addSection: (token: string, formId: string, body: { code: string; titleAr: string; titleEn: string | null; minBlocks: number; maxBlocks: number }) =>
      request<FormSectionDto>(`/api/forms/${formId}/sections`, { method: 'POST', body: JSON.stringify(body) }, token),
    updateSection: (token: string, formId: string, sectionId: string, body: { titleAr: string; titleEn: string | null; minBlocks: number; maxBlocks: number }) =>
      request<FormSectionDto>(`/api/forms/${formId}/sections/${sectionId}`, { method: 'PUT', body: JSON.stringify(body) }, token),
    removeSection: (token: string, formId: string, sectionId: string) =>
      request<void>(`/api/forms/${formId}/sections/${sectionId}`, { method: 'DELETE' }, token),
    moveFieldToSection: (token: string, formId: string, fieldId: string, sectionId: string | null) =>
      request<void>(`/api/forms/${formId}/fields/${fieldId}/section`, { method: 'PUT', body: JSON.stringify({ sectionId }) }, token),
    updateFieldSettings: (
      token: string, formId: string, fieldId: string,
      body: { formula: string | null; rowFilterJson: string | null; visibleWhenAttribute: string | null; optionsProvider: string | null },
    ) => request<void>(`/api/forms/${formId}/fields/${fieldId}/settings`, { method: 'PUT', body: JSON.stringify(body) }, token),
    setFlags: (token: string, formId: string, isSystem: boolean, isHidden: boolean) =>
      request<void>(`/api/forms/${formId}/flags`, { method: 'PUT', body: JSON.stringify({ isSystem, isHidden }) }, token),
    optionsProviders: (token: string) => request<{ key: string; description: string }[]>(`/api/forms/options-providers`, {}, token),

    /** Takes the complete ordered list of ACTIVE field ids - a partial list is rejected. */
    reorderFields: (token: string, formId: string, orderedFieldIds: string[]) =>
      request<void>(
        `/api/forms/${formId}/fields/order`,
        { method: 'PUT', body: JSON.stringify({ orderedFieldIds }) },
        token,
      ),

    // --- Risky field edits: each needs its own explicit confirmation in the UI ---

    /** Real ALTER TABLE ... RENAME COLUMN on a published form. Data survives; external
     *  references to the old column name do not. */
    renameFieldCode: (token: string, formId: string, fieldId: string, newCode: string) =>
      request<void>(
        `/api/forms/${formId}/fields/${fieldId}/code`,
        { method: 'PUT', body: JSON.stringify({ newCode }) },
        token,
      ),

    /** Rejected with a 400 naming the offending record ids if any stored value can't convert. */
    changeFieldType: (
      token: string,
      formId: string,
      fieldId: string,
      input: { newFieldType: string; optionsJson?: string | null; lookupFormDefinitionId?: string | null },
    ) =>
      request<void>(
        `/api/forms/${formId}/fields/${fieldId}/type`,
        { method: 'PUT', body: JSON.stringify(input) },
        token,
      ),

    /** Recommended way to take a field off a form: keeps the column and all history. */
    archiveField: (token: string, formId: string, fieldId: string) =>
      request<void>(`/api/forms/${formId}/fields/${fieldId}/archive`, { method: 'POST' }, token),

    restoreField: (token: string, formId: string, fieldId: string) =>
      request<void>(`/api/forms/${formId}/fields/${fieldId}/restore`, { method: 'POST' }, token),

    /** Irreversible: drops the column and its data. confirmFieldCode must equal the field's
     *  own code on a published form; it's not required on a form that was never published,
     *  where there is no column and no data yet. */
    deleteField: (token: string, formId: string, fieldId: string, confirmFieldCode?: string) => {
      const query = confirmFieldCode ? `?confirmFieldCode=${encodeURIComponent(confirmFieldCode)}` : '';
      return request<void>(`/api/forms/${formId}/fields/${fieldId}${query}`, { method: 'DELETE' }, token);
    },

    startNewVersion: (token: string, formId: string) =>
      request<{ id: string }>(`/api/forms/${formId}/versions`, { method: 'POST' }, token),

    delete: (token: string, formId: string) =>
      request<void>(`/api/forms/${formId}`, { method: 'DELETE' }, token),

    setAllowedRoles: (token: string, formId: string, roleIds: string[]) =>
      request<void>(`/api/forms/${formId}/allowed-roles`, { method: 'PUT', body: JSON.stringify({ roleIds }) }, token),

    setAllowedUsers: (token: string, formId: string, userIds: string[]) =>
      request<void>(`/api/forms/${formId}/allowed-users`, { method: 'PUT', body: JSON.stringify({ userIds }) }, token),
  },

  submissions: {
    /** idempotencyKey: the server saves it only once; enteredAtUtc: when Send was pressed (offline queue, §9.6). */
    submit: (token: string, formId: string, values: Record<string, unknown>, options: { idempotencyKey?: string; enteredAtUtc?: string } = {}) =>
      request<{ id: string }>(
        `/api/forms/${formId}/submissions`,
        {
          method: 'POST',
          body: JSON.stringify(values),
          headers: {
            ...(options.idempotencyKey ? { 'Idempotency-Key': options.idempotencyKey } : {}),
            ...(options.enteredAtUtc ? { 'X-Entered-At': options.enteredAtUtc } : {}),
          },
        },
        token,
      ),

    list: (
      token: string,
      formId: string,
      page = 1,
      pageSize = 25,
      filter?: { fieldCode: string; value: string },
    ) => {
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
      if (filter) {
        params.set('filterFieldCode', filter.fieldCode);
        params.set('filterValue', filter.value);
      }
      return request<PagedResult<DynamicRow>>(`/api/forms/${formId}/submissions?${params}`, {}, token);
    },

    /** Administrators: replace a record's values (the whole record, same shape as submit). */
    update: (token: string, formId: string, recordId: string, values: Record<string, unknown>) =>
      request<void>(
        `/api/forms/${formId}/submissions/${recordId}`,
        { method: 'PUT', body: JSON.stringify(values) },
        token,
      ),

    /** Administrators: remove a record (soft delete - recoverable from the database). */
    delete: (token: string, formId: string, recordId: string) =>
      request<void>(`/api/forms/${formId}/submissions/${recordId}`, { method: 'DELETE' }, token),

    /** Records tab (administrators, §9.4): filters run on the server; items carry sender, time, Lookup labels. */
    search: (token: string, formId: string, query: RecordQuery) => {
      const params = new URLSearchParams({ page: String(query.page ?? 1), pageSize: String(query.pageSize ?? 25) });
      if (query.q?.trim()) params.set('q', query.q.trim());
      if (query.from) params.set('from', query.from);
      if (query.to) params.set('to', query.to);
      if (query.dateField) params.set('dateField', query.dateField);
      for (const [code, value] of Object.entries(query.filters ?? {})) if (value !== '' && value !== undefined) params.append('f', `${code}:${value}`);
      return request<RecordsPageDto>(`/api/forms/${formId}/submissions?${params}`, {}, token);
    },

    /** One record with its attachments - its sender or an administrator (§9.3). */
    get: (token: string, formId: string, recordId: string) =>
      request<RecordDetailDto>(`/api/forms/${formId}/submissions/${recordId}`, {}, token),

    /** My entries (§9.3): the caller's own records, newest first; pass the last page's nextBefore. */
    mine: (token: string, options: { formId?: string | null; before?: string | null; pageSize?: number } = {}) => {
      const params = new URLSearchParams({ pageSize: String(options.pageSize ?? 20) });
      if (options.formId) params.set('formId', options.formId);
      if (options.before) params.set('before', options.before);
      return request<MyEntriesPageDto>(`/api/submissions/mine?${params}`, {}, token);
    },
  },

  stageProgress: {
    project: (token: string, projectId: string) => request<ProjectProgressDto>(`/api/stage-progress/projects/${projectId}`, {}, token),
    reopen: (token: string, projectId: string, body: { stageCode: string; substepCode: string | null; levelId: string | null; side: number | null; reason: string | null }) =>
      request<void>(`/api/stage-progress/projects/${projectId}/reopen`, { method: 'POST', body: JSON.stringify(body) }, token),
  },

  analytics: {
    /** The whole Executive Overview in one call; projectId and phase narrow everything but the project list. */
    executiveOverview: (token: string, projectId?: string | null, phase?: string | null) =>
      request<ExecutiveOverviewDto>(
        `/api/analytics/executive-overview${overviewQuery(projectId, phase)}`,
        {},
        token,
      ),

    /**
     * The same data without a sign-in, opened by the share key the public link carries. The
     * key travels in a header, never in the address sent to the server. No token is passed,
     * so a failure here never triggers a token refresh or touches the signed-in session.
     */
    publicExecutiveOverview: (shareKey: string, projectId?: string | null, phase?: string | null) =>
      request<ExecutiveOverviewDto>(
        `/api/public/executive-overview${overviewQuery(projectId, phase)}`,
        { headers: { 'X-Dashboard-Key': shareKey } },
      ),

    /** The key for the public link, or null when public viewing is switched off on the server. */
    executiveOverviewShareKey: (token: string) =>
      request<{ key: string | null }>('/api/analytics/executive-overview/share-key', {}, token),
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
      // The server's own limits (UploadFileCommandValidator), checked first so the person gets
      // a clear, translated reason instead of a rejected upload.
      if (!UPLOAD_TYPES.has(file.type.toLowerCase()))
        throw new ApiError('Only JPEG, PNG, HEIC, WEBP images and PDF files are accepted.', 400, undefined, 'upload.fileType');
      const toSend = await shrinkImageForUpload(file);
      if (toSend.size > UPLOAD_MAX_BYTES)
        throw new ApiError('File must be under 20MB.', 400, undefined, 'upload.tooLarge');
      const formData = new FormData();
      formData.append('fieldCode', fieldCode);
      formData.append('file', toSend);

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
        else if (getTokens()) throw serverBusyError();
      }
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new ApiError(problem?.detail ?? `Upload failed with status ${response.status}`, response.status, problem?.errors);
      }
      return response.json();
    },
  },

  /** Stakeholder reports (administrators). */
  messagingSchedule: {
    get: (token: string) => request<MessagingScheduleDto>('/api/messaging-schedule', {}, token),
    save: (token: string, schedule: MessagingScheduleDto) =>
      request<MessagingScheduleDto>('/api/messaging-schedule', { method: 'PUT', body: JSON.stringify(schedule) }, token),
  },

  reports: {
    overview: (token: string) => request<ReportsOverviewDto>('/api/reports', {}, token),

    saveRecipient: (
      token: string,
      input: Omit<ReportRecipientDto, 'id'>,
      id?: string,
    ) =>
      request<ReportRecipientDto>(
        id ? `/api/reports/recipients/${id}` : '/api/reports/recipients',
        { method: id ? 'PUT' : 'POST', body: JSON.stringify(input) },
        token,
      ),

    deleteRecipient: (token: string, id: string) =>
      request<void>(`/api/reports/recipients/${id}`, { method: 'DELETE' }, token),

    /** Makes a report now (up to a couple of minutes with the analysis). deliver false = preview only. */
    run: (token: string, input: { kind: ReportKind; to: string | null; withAnalysis: boolean; deliver: boolean }) =>
      request<ReportRunDto>('/api/reports/run', { method: 'POST', body: JSON.stringify(input) }, token),

    /** A 10-minute download link for a stored PDF. */
    download: (token: string, runId: string, phone: boolean) =>
      request<{ url: string }>(`/api/reports/runs/${runId}/download?phone=${phone}`, {}, token),
  },

  /** Daily form assignments. Everything but `mine` is for administrators. */
  assignments: {
    mine: (token: string) => request<MyAssignmentsDto>('/api/form-assignments/mine', {}, token),

    list: (token: string) => request<FormAssignmentDto[]>('/api/form-assignments', {}, token),

    days: (token: string, filter: { from: string; to: string; userId?: string | null; formId?: string | null }) => {
      const params = new URLSearchParams({ from: filter.from, to: filter.to });
      if (filter.userId) params.set('userId', filter.userId);
      if (filter.formId) params.set('formId', filter.formId);
      return request<AssignmentDayDto[]>(`/api/form-assignments/days?${params}`, {}, token);
    },

    create: (
      token: string,
      input: { formDefinitionId: string; userIds: string[]; startDate: string; endDate: string | null; note: string | null },
    ) =>
      request<{ created: number; alreadyAssigned: string[] }>(
        '/api/form-assignments',
        { method: 'POST', body: JSON.stringify(input) },
        token,
      ),

    stop: (token: string, id: string) => request<void>(`/api/form-assignments/${id}/stop`, { method: 'POST' }, token),

    /** Reminds everyone still pending today; once per person, form and day (redesign 23). */
    remindPending: (token: string) =>
      request<{ sent: number; failed: number; alreadyReminded: number; noChannel: number }>('/api/form-assignments/remind-pending', { method: 'POST' }, token),
  },

  users: {
    list: (token: string) => request<UserSummaryDto[]>('/api/users', {}, token),

    /** WhatsApp number; null removes it. */
    setPhoneNumber: (token: string, userId: string, phoneNumber: string | null) =>
      request<void>(`/api/users/${userId}/phone`, { method: 'PUT', body: JSON.stringify({ phoneNumber }) }, token),

    /** Sends a sample reminder; the answer says whether Meta accepted it. */
    sendWhatsAppTest: (token: string, userId: string) =>
      request<{ success: boolean; messageId: string | null; error: string | null }>(
        `/api/users/${userId}/whatsapp-test`,
        { method: 'POST' },
        token,
      ),

    /** A one-time "Connect Telegram" link for this user, to send them (valid 7 days). */
    createTelegramLink: (token: string, userId: string) =>
      request<TelegramLinkDto>(`/api/users/${userId}/telegram-link`, { method: 'POST' }, token),

    unlinkTelegram: (token: string, userId: string) =>
      request<void>(`/api/users/${userId}/telegram`, { method: 'DELETE' }, token),

    /** Sends a sample message; the answer says whether Telegram accepted it. */
    sendTelegramTest: (token: string, userId: string) =>
      request<{ success: boolean; messageId: string | null; error: string | null }>(
        `/api/users/${userId}/telegram-test`,
        { method: 'POST' },
        token,
      ),

    setRoles: (token: string, userId: string, roleIds: string[]) =>
      request<void>(`/api/users/${userId}/roles`, { method: 'PUT', body: JSON.stringify({ roleIds }) }, token),

    setActiveStatus: (token: string, userId: string, isActive: boolean) =>
      request<void>(`/api/users/${userId}/active-status`, { method: 'PUT', body: JSON.stringify({ isActive }) }, token),

    /** Sets a new password for someone who forgot theirs, and signs them out everywhere. */
    resetPassword: (token: string, userId: string, newPassword: string) =>
      request<void>(`/api/users/${userId}/password`, { method: 'POST', body: JSON.stringify({ newPassword }) }, token),
  },

  /** The signed-in user's own settings (any role). */
  me: {
    telegramStatus: (token: string) => request<TelegramStatusDto>('/api/me/telegram', {}, token),

    /** A fresh link to open in Telegram; replaces any earlier one. */
    createTelegramLink: (token: string) => request<TelegramLinkDto>('/api/me/telegram/link', { method: 'POST' }, token),

    unlinkTelegram: (token: string) => request<void>('/api/me/telegram', { method: 'DELETE' }, token),
  },

  roles: {
    list: (token: string) => request<RoleDto[]>('/api/roles', {}, token),

    create: (token: string, name: string, description?: string) =>
      request<{ id: string }>('/api/roles', { method: 'POST', body: JSON.stringify({ name, description: description ?? null }) }, token),
  },
};
