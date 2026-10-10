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
  /** Conditional-visibility ("branching"): the Code of another field on the SAME form
   *  version that controls whether this field is shown. Null = always visible (default). */
  visibleWhenFieldCode: string | null;
  /** JSON-encoded string[] - the controlling field's values that make this field visible.
   *  Only meaningful when visibleWhenFieldCode is set. Parse before use. */
  visibleWhenValuesJson: string | null;
  /** Filtered/cascading Lookup: the Code of another field on the SAME form version whose
   *  current value narrows this Lookup's candidate rows down to the ones belonging to it
   *  (e.g. "zone" filtered by "project"). Only meaningful when fieldType is Lookup. By
   *  convention the target form must have an active field with this identical Code. Null =
   *  show every target-form row (default, unchanged behaviour). */
  filterByFieldCode: string | null;
  /** Dynamic options: this Dropdown's option list is computed live as the distinct values
   *  currently submitted for DynamicOptionsSourceFieldCode on this form (not a sibling on
   *  THIS form - see FieldDefinition.DynamicOptionsSourceFieldCode on the backend). Null =
   *  use this field's own static optionsJson (default, unchanged behaviour). Only meaningful
   *  when fieldType is Dropdown. */
  dynamicOptionsSourceFormDefinitionId: string | null;
  dynamicOptionsSourceFieldCode: string | null;
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

// Mirrors Platform.Application.Analytics.Queries.GetExecutiveOverview's DTOs - same
// hand-kept-in-sync convention as the rest of this file. Shares are 0..1; a null share
// means "no reports to judge by", which the page shows as a dash, never as 0%.
// Dates are plain calendar dates ("2026-10-10"), already in Saudi local time.

export type OverviewPhase = 'paperwork' | 'excavation' | 'foundation' | 'structural' | 'mep';
export type OverviewScheduleStatus = 'ahead' | 'behind' | 'on_track' | 'finished' | 'unknown';

export interface OverviewTilesDto {
  projectCount: number;
  activeCount: number;
  onHoldCount: number;
  completedCount: number;
  workComplete: number;
  activeBehindCount: number;
  planMetLast14Days: number | null;
  planMetPrevious14Days: number | null;
  reportsLast14Days: number;
  workersOnSite: number | null;
  workersOnSiteDate: string | null;
  delayedShareLast30Days: number | null;
  delayedReportsLast30Days: number;
  reportsLast30Days: number;
}

export interface OverviewProjectDto {
  id: string;
  code: string;
  name: string;
  city: string | null;
  manager: string | null;
  status: string | null;
  currentPhases: OverviewPhase[];
  workComplete: number;
  /** Share of the start-to-expected-completion span already passed; null without both dates. */
  timeElapsed: number | null;
  scheduleStatus: OverviewScheduleStatus;
  /** Work complete minus time elapsed, in whole percentage points. */
  scheduleGapPoints: number | null;
  phases: { phase: OverviewPhase; complete: number }[];
  planMet: number;
  reportCount: number;
  lastReportDate: string;
  daysSinceLastReport: number;
}

export interface OverviewWeekPlanMetDto {
  weekStart: string;
  planMet: number | null;
  reportCount: number;
}

export interface OverviewWeekWorkersDto {
  weekStart: string;
  averageWorkersPerDay: number;
  reportingDays: number;
}

export interface OverviewWeatherDto {
  weather: string;
  planMet: number;
  reportCount: number;
}

export interface OverviewLabelCountDto {
  label: string;
  count: number;
}

export interface OverviewStockMaterialDto {
  material: string;
  /** True when the material was typed into the form's "other" box rather than picked from the list. */
  isCustomMaterial: boolean;
  unit: string | null;
  received: number;
  issued: number;
  onHand: number;
  issuedShare: number;
}

export interface OverviewProblemDto {
  date: string;
  phase: OverviewPhase;
  description: string;
  projectCode: string;
  projectName: string;
}

export interface ExecutiveOverviewDto {
  asOf: string;
  dataFrom: string | null;
  selectedProjectId: string | null;
  projectsWithoutReports: number;
  tiles: OverviewTilesDto;
  projects: OverviewProjectDto[];
  planMetByWeek: OverviewWeekPlanMetDto[];
  workersByWeek: OverviewWeekWorkersDto[];
  planMetByWeather: OverviewWeatherDto[];
  delayedReportCount: number;
  delayCauses: OverviewLabelCountDto[];
  stockByMaterial: OverviewStockMaterialDto[];
  stockIssueCount: number;
  issueReasons: OverviewLabelCountDto[];
  problemReportCount: number;
  latestProblems: OverviewProblemDto[];
}
