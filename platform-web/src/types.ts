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
  | 'Attachment'
  /** Several options; stored as a list (2026-11-10). */
  | 'MultiSelect'
  /** Several rows of another form; stored as a list of ids (2026-11-10). */
  | 'MultiLookup';

export type FormStatus = 'Draft' | 'Published' | 'Retired';

export interface FieldDefinitionDto {
  id: string;
  code: string;
  /** The Arabic label (it also names the reporting view's column). */
  label: string;
  /** The English label (redesign §9.1); null/absent until filled in - show `label` then. */
  labelEn?: string | null;
  /** The step this field is filled in (§9.2); null = the first step. */
  stepId?: string | null;
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
  /** The repeatable section this field belongs to (null = the form's header). */
  sectionId?: string | null;
  /** With visibleWhenFieldCode: compare this attribute of the controlling field's chosen
   *  option (e.g. a stage's unit_pick) instead of its value. */
  visibleWhenAttribute?: string | null;
  /** Lookup: only rows whose field is one of the values (JSON {field, values}). */
  rowFilterJson?: string | null;
  /** Number/Decimal: read-only, calculated by the server from sibling number fields. */
  formula?: string | null;
  /** The server-side options provider this choice field takes its options from. */
  optionsProvider?: string | null;
}

/** A repeatable section (2026-11-10): its fields are filled once per block. */
export interface FormSectionDto {
  id: string;
  code: string;
  titleAr: string;
  titleEn: string | null;
  minBlocks: number;
  maxBlocks: number;
}

export interface FormVersionDto {
  id: string;
  versionNumber: number;
  status: FormStatus;
  publishedAtUtc: string | null;
  fields: FieldDefinitionDto[];
  /** Filling steps in order (§9.2); empty = one step titled with the form name. */
  steps?: FormStepDto[];
  /** Repeatable sections (2026-11-10). */
  sections?: FormSectionDto[];
}

export interface FormStepDto {
  id: string;
  titleAr: string;
  titleEn: string | null;
  displayOrder: number;
}

export interface FormSummaryDto {
  id: string;
  code: string;
  name: string;
  /** English name (§9.1); null/absent until filled in - show `name` then. */
  nameEn?: string | null;
  moduleName: string;
  status: FormStatus;
  /** Hidden from the menus (records and builder still open for administrators). */
  isHidden?: boolean;
  /** A system form: progress figures read it, so its fields can't be renamed or removed. */
  isSystem?: boolean;
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
  nameEn?: string | null;
  isSystem?: boolean;
  isHidden?: boolean;
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
  /** International digits without "+", for WhatsApp; null = no messages. */
  phoneNumber: string | null;
  /** The user has connected Telegram (pressed Start on their link). */
  telegramLinked: boolean;
  /** Linked to a Telegram group (set up by an administrator) rather than a private chat. */
  telegramIsGroup: boolean;
}

/** The signed-in user's own Telegram connection (Settings). */
export interface TelegramStatusDto {
  /** The server has a bot; when false the Settings section is hidden. */
  configured: boolean;
  linked: boolean;
  botUsername: string | null;
}

/** A one-time "Connect Telegram" link (t.me/bot?start=code), valid 7 days. */
export interface TelegramLinkDto {
  url: string;
  /** For an administrator: Telegram asks which group to add the bot to. */
  groupUrl: string;
  expiresAtUtc: string;
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
  /** English label (§9.1), stored inside OptionsJson. */
  labelEn?: string | null;
}

// Mirrors Platform.Application.Analytics.Queries.GetExecutiveOverview's DTOs - same
// hand-kept-in-sync convention as the rest of this file. Shares are 0..1; a null share
// means "no reports to judge by", which the page shows as a dash, never as 0%.
// Dates are plain calendar dates ("2026-10-10"), already in Saudi local time.

/** A stage code from the projects' stage plans (2026-11-10), or an old report's phase. */
export type OverviewPhase = string;

/** A stage of the plans in scope, in plan order, with its names. */
export interface OverviewStageInfoDto {
  code: string;
  nameAr: string;
  nameEn: string;
}
export type OverviewScheduleStatus = 'ahead' | 'behind' | 'on_track' | 'finished' | 'unknown';

/** Thresholds live on the server (OverviewKpiThresholds) - the page only colours by them. */
export type OverviewKpiTone = 'good' | 'warn' | 'bad';

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
  /** With a stage selected: that stage's days earned over planned; null without one. */
  stageWorkComplete: number | null;
  stageEarnedDays: number;
  stagePlannedDays: number;
  /** Working days on which nothing in scope met its plan, out of the days worked. */
  lateDays: number;
  workedDays: number;
  lateShare: number | null;
  lateDaysTone: OverviewKpiTone | null;
  lateShareTone: OverviewKpiTone | null;
  /** At the last 30 days' pace; for a finished stage, its last report. */
  forecastFinish: string | null;
  /** Expected completion (whole project) or the stage's planned end (+10% slack). */
  forecastTarget: string | null;
  forecastDaysLate: number | null;
  forecastFinished: boolean;
  forecastTone: OverviewKpiTone | null;
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
  phases: { phase: OverviewPhase; complete: number; nameAr?: string | null; nameEn?: string | null }[];
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
  selectedPhase: OverviewPhase | null;
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
  /** The stages of the plans in scope, for the filter and every stage label. */
  stages?: OverviewStageInfoDto[] | null;
}

// ------------------------------------------------------------------- daily form assignments
// Dates ("2026-11-03") are Saudi calendar days; *AtUtc values are UTC instants.

/** filled: submitted that day; missed: the day is over with nothing; pending: today, nothing yet. */
export type AssignmentDayStatus = 'filled' | 'missed' | 'pending';

export interface AssignmentDayDto {
  assignmentId: string;
  userId: string;
  userName: string;
  formId: string;
  formName: string;
  date: string;
  assignedAtUtc: string;
  assignedByName: string | null;
  status: AssignmentDayStatus;
  filledAtUtc: string | null;
  submissionCount: number;
  /** WhatsApp reminders about this day (tracking page only). */
  remindersSent: number;
  remindersFailed: number;
  /** That day's first entry, when filled (the grid cell opens it). */
  recordId?: string | null;
  /** When the person was last reminded about that day (automatic or manual). */
  lastRemindedAtUtc?: string | null;
}

export interface MyAssignmentsDto {
  today: string;
  days: AssignmentDayDto[];
}

export interface FormAssignmentDto {
  id: string;
  userId: string;
  userName: string;
  formId: string;
  formName: string;
  startDate: string;
  endDate: string | null;
  note: string | null;
  assignedAtUtc: string;
  assignedByName: string | null;
  isActive: boolean;
  daysDue: number;
  daysFilled: number;
}

// ------------------------------------------------------------------- stakeholder reports
// Mirrors Platform.Application.Reports.ReportDtos.

export type ReportKind = 'daily' | 'weekly' | 'monthly';

export interface ReportRecipientDto {
  id: string;
  name: string;
  /** International digits without "+". */
  phoneNumber: string;
  receivesDaily: boolean;
  receivesWeekly: boolean;
  receivesMonthly: boolean;
  isActive: boolean;
}

export interface ReportDeliveryDto {
  recipientName: string;
  phoneNumber: string;
  /** running | sent | failed */
  status: string;
  error: string | null;
  sentAtUtc: string | null;
}

export interface ReportRunDto {
  id: string;
  kind: ReportKind;
  periodStart: string;
  periodEnd: string;
  /** schedule | preview | send-now */
  trigger: string;
  /** running | done | failed */
  status: string;
  /** written | skipped-budget | not-configured | failed | off */
  analysis: string | null;
  inputTokens: number;
  outputTokens: number;
  costUsd: number;
  error: string | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
  hasPdf: boolean;
  deliveries: ReportDeliveryDto[];
}

export interface ReportsOverviewDto {
  analysisConfigured: boolean;
  pdfConfigured: boolean;
  whatsAppConfigured: boolean;
  model: string;
  sendTime: string;
  monthlyBudgetUsd: number;
  spentThisMonthUsd: number;
  recipients: ReportRecipientDto[];
  runs: ReportRunDto[];
}

// ---------------------------------------------------------------- redesign phase 7 (§9.3, §9.4)

/** A submitted record with who sent it and when; values keyed by field code, typed as stored. */
export interface RecordItemDto {
  id: string;
  formId: string;
  createdAtUtc: string;
  createdByUserId: string;
  createdByName: string | null;
  values: Record<string, unknown>;
  /** Lookup field code -> the chosen row's label. */
  lookupLabels: Record<string, string>;
  attachmentCount: number;
  enteredAtUtc?: string | null;
  /** Repeatable sections: section code -> the blocks in order. */
  sections?: Record<string, RecordBlockDto[]> | null;
}

/** One block of a repeatable section. Labels hold what each choice reads as; a provider
 *  label is "Arabic\u001FEnglish" when both exist (forms/recordAnswers splits it). */
export interface RecordBlockDto {
  index: number;
  values: Record<string, unknown>;
  labels: Record<string, string>;
}

export interface RecordsPageDto {
  items: RecordItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface RecordDetailDto {
  record: RecordItemDto;
  attachments: FileMetadataDto[];
}

export interface MyEntriesPageDto {
  items: RecordItemDto[];
  nextBefore: string | null;
}

export interface RecordQuery {
  page?: number;
  pageSize?: number;
  q?: string;
  /** yyyy-mm-dd (Saudi days); the end day is included. */
  from?: string;
  to?: string;
  /** A DateTime field the range applies to; without it, when the record was sent. */
  dateField?: string;
  /** Field code -> value (equality). */
  filters?: Record<string, string>;
}

/** An option of a choice field from the options endpoint: `id` is what gets submitted (a row
 *  id for a Lookup, the option value for a provider field). */
export interface ChoiceOptionDto {
  id: string;
  label: string;
  labelEn?: string | null;
  /** Settings other fields' visibility (or a block rule) depends on. */
  attributes?: Record<string, string> | null;
}

/** One unit of a project's stage plan with its progress (api/stage-progress). */
export interface StageUnitDto {
  key: string;
  stageCode: string;
  substepCode: string | null;
  levelId: string | null;
  side: number | null;
  stageNameAr: string;
  stageNameEn: string;
  substepNameAr: string | null;
  substepNameEn: string | null;
  levelNameAr: string | null;
  levelNameEn: string | null;
  plannedDays: number;
  earnedDays: number;
  finished: boolean;
  actualStart: string | null;
  actualFinish: string | null;
  plannedFinish: string | null;
  /** Actual minus planned finish in working days; positive = late. */
  lateByDays?: number | null;
}

export interface ProjectProgressDto {
  projectId: string;
  code: string;
  name: string;
  status: string | null;
  startDate: string | null;
  earnedDays: number;
  plannedDays: number;
  workComplete: number;
  plannedShare: number | null;
  elapsedWorkingDays: number;
  finishFastest: string | null;
  finishSlowest: string | null;
  finishTarget: string | null;
  skeletonFastest: number | null;
  skeletonSlowest: number | null;
  totalFastest: number;
  totalSlowest: number;
  units: StageUnitDto[];
}
