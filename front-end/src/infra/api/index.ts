import { employeeTemplateApi } from './modules/employeeTemplateApi'
import { hiringWorkflowApi } from './modules/hiringWorkflowApi'
import { employeeRuntimeApi } from './modules/employeeRuntimeApi'
import { skillCatalogApi } from './modules/skillCatalogApi'
import { migrationApi } from './modules/migrationApi'
import { settingsApi } from './modules/settingsApi'

export const api = {
  employeeTemplate: employeeTemplateApi,
  hiringWorkflow: hiringWorkflowApi,
  employeeRuntime: employeeRuntimeApi,
  skillCatalog: skillCatalogApi,
  migration: migrationApi,
  settings: settingsApi,
}

export { ApiClientError } from './httpClient'

export type { HiringSandboxItem } from './modules/settingsApi'

export type {
  ApiResponseEnvelope,
  QueryParams,
  QueryValue,
  RequestOptions,
} from './types'

export type {
  TemplateLatestVersion,
  EmployeeTemplateCard,
  EmployeeTemplateListData,
  TemplateResponsibilityBoundary,
  TemplatePrerequisite,
  TemplateCta,
  EmployeeTemplatePackageSkill,
  EmployeeTemplateDetail,
  HireTemplateRequest,
  HireTemplateResult,
  HiringStatusResult,
  FixtureTemplateHireResult,
} from './modules/employeeTemplateApi'

export {
  HiringCollectionPhase,
  HiringCollectionStage,
  HiringAuditDecision,
  HiringTodoStatus,
  HiringStageReadinessStatus,
} from './modules/hiringWorkflowApi'

export type {
  HiringCollectionPhaseType,
  HiringCollectionStageType,
  HiringAuditDecisionType,
  StageSkillMapping,
  HiringStageCompletion,
  WorkflowTodo,
  DispatchArtifact,
  DispatchTodoResult,
  DispatchCallback,
  StageReadiness,
  DiagnosticTodo,
  DiagnosticReport,
  WorkflowRuntimeFacts,
  ConfigGovernanceFile,
  ConfigGovernanceState,
  HiringCliToolConfig,
  HiringMcpServerConfig,
  HiringExternalSystemConfig,
  HiringMcpConnectivityTestResult,
  HiringConversationMaterial,
  HiringMaterialFile,
  StartHiringConversationResult,
  HiringConversationMessageRequest,
  HiringConversationMessage,
  HiringStagePreview,
  HiringConversationResult,
  HiringAuditDecisionRequest,
  HiringAuditDecisionResult,
  HiringAuditLog,
  HiringFinalizeResult,
  HiringWorkflowState,
  HiringArtifactsDownloadData,
  HandoffItem,
  PersistedChatFile,
  PersistedPackageStructure,
  RuntimeStateSaveRequest,
  RuntimeStateStage,
} from './modules/hiringWorkflowApi'

export type {
  EmployeeCapability,
  CreatorRef,
  EmployeeSummary,
  EmployeeDetail,
  ImPlatformId,
  ImConnectionMode,
  FeishuChannelEffectiveConfig,
  DingTalkChannelEffectiveConfig,
  DingTalkChannelConfigRequest,
  ImConfigRequest,
  ImConfigResult,
  UpdateEmployeeLifecycleRequest,
  CreatePersonalCloneRequest,
  InstanceChatMessage,
  EvaluationScenario,
  EvaluationReadiness,
  EvaluationQuestionCard,
  EvaluationTestcaseOutline,
  EvaluationReportSummary,
  EvaluationAssetRef,
  EvaluationState,
  EvaluationSandboxConversationState,
  EvaluationSandboxMessageRequest,
  AiEvaluationDecisionRequest,
  EvaluationOnboardingDecisionRequest,
  EvaluationSandboxConnectionResult,
  EvaluationDimensionScore,
  EvaluationVerdictPayload,
  EvaluationVerdictSyncRequest,
  EvaluationVerdictSyncResult,
  EvaluationWorkspaceStep,
  EvaluationWorkspaceStatus,
  EvaluationTraceContentResult,
} from './modules/employeeRuntimeApi'

export type {
  StoreSkillItem,
  RecommendedStoreSkillItem,
  StoreSkillListData,
} from './modules/skillCatalogApi'

export type {
  LocalStateEmployeeMigrationItem,
  LocalStateMigrationRequest,
  LocalStateMigrationResult,
} from './modules/migrationApi'
