// Mirrors Platform.Domain.Forms.Enums.FieldType and Platform.Application.Forms.Dtos
// on the backend. Keep these in sync by hand for now - there's no shared-schema
// generation between the C# and TS sides yet (a real OpenAPI-client-generation step
// would remove this duplication; worth adding once the API surface stabilizes).

export type FieldType =
  | 'ShortText'
  | 'LongText'
  | 'Number'
  | 'Decimal'
  | 'Boolean'
  | 'DateTime'
  | 'Dropdown'
  | 'Lookup'
  | 'Attachment';

export type FormStatus = 'Draft' | 'Published' | 'Retired';

export interface FieldDefinitionDto {
  id: string;
  code: string;
  label: string;
  fieldType: FieldType;
  isRequired: boolean;
  isActive: boolean;
  displayOrder: number;
  /** JSON-encoded string of {value,label}[] - only meaningful when fieldType is Dropdown. Parse before use. */
  optionsJson: string | null;
  /** Only meaningful when fieldType is Lookup. */
  lookupFormDefinitionId: string | null;
  validationRulesJson: string | null;
}

export interface FormVersionDto {
  id: string;
  versionNumber: number;
  status: FormStatus;
  publishedAtUtc: string | null;
  fields: FieldDefinitionDto[];
}

export interface FormSummaryDto {
  id: string;
  code: string;
  name: string;
  moduleName: string;
  status: FormStatus;
}

export interface FormDefinitionDto {
  id: string;
  code: string;
  name: string;
  description: string | null;
  moduleName: string;
  status: FormStatus;
  tableName: string | null;
  draftVersion: FormVersionDto | null;
  publishedVersion: FormVersionDto | null;
}

/** Raw submission row - values are keyed by FieldDefinition.code, same shape the backend returns. */
export interface DynamicRow {
  id: string;
  values: Record<string, unknown>;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface TokenPair {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
}

export interface CurrentUserDto {
  id: string;
  email: string;
  displayName: string;
  departmentId: string | null;
  roles: string[];
}

export interface DropdownOption {
  value: string;
  label: string;
}
