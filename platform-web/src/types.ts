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
  allowedRoleIds: string[];
  allowedUserIds: string[];
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

export interface AvailableTransitionDto {
  code: string;
  label: string;
}

export interface WorkflowHistoryEntryDto {
  fromStateLabel: string | null;
  toStateLabel: string;
  transitionLabel: string | null;
  executedByUserId: string;
  executedAtUtc: string;
  comment: string | null;
}

export interface WorkflowStatusDto {
  recordId: string;
  workflowCode: string;
  currentStateCode: string;
  currentStateLabel: string;
  isFinal: boolean;
  availableTransitions: AvailableTransitionDto[];
  history: WorkflowHistoryEntryDto[];
}

export interface FileMetadataDto {
  id: string;
  fieldCode: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
}

export interface UserRoleSummary {
  id: string;
  name: string;
}

export interface UserSummaryDto {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  roles: UserRoleSummary[];
}

export interface RoleDto {
  id: string;
  name: string;
  description: string | null;
  isSystemRole: boolean;
}

export interface DropdownOption {
  value: string;
  label: string;
}

// Mirrors Platform.Application.Analytics's query handler DTOs - same hand-kept-in-sync
// convention as the rest of this file.

export interface ProjectProgressDto {
  projectId: string;
  projectCode: string;
  projectName: string;
  status: string | null;
  startDateUtc: string | null;
  expectedCompletionUtc: string | null;
  /** Null means "not enough information" (a missing date, or a non-positive span) - render "—", not 0%/100%. */
  percentComplete: number | null;
}

export interface ProjectCrewCountDto {
  projectId: string;
  projectCode: string;
  projectName: string;
  logDate: string;
  totalHeadcount: number;
}

export interface ProjectWeatherSummaryDto {
  projectId: string;
  projectCode: string;
  projectName: string;
  totalDays: number;
  impactedDays: number;
}

export interface OverdueTaskDto {
  taskId: string;
  projectId: string;
  projectCode: string;
  projectName: string;
  taskReference: string;
  description: string;
  assignedTo: string;
  priority: string;
  dueDateUtc: string;
}

export type StockMovementType = 'GoodsReceipt' | 'MaterialIssue' | 'StockTransfer' | 'StockAdjustment';

export interface StockMovementBreakdownDto {
  movementType: StockMovementType;
  locationId: string;
  locationName: string;
  materialId: string;
  materialCode: string;
  materialName: string;
  totalQuantity: number;
}
