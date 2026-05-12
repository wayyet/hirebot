import { employeeTemplateApi } from './modules/employeeTemplateApi'
import { hiringWorkflowApi } from './modules/hiringWorkflowApi'
import { employeeRuntimeApi } from './modules/employeeRuntimeApi'
import { collaborationApi } from './modules/collaborationApi'
import { skillCatalogApi } from './modules/skillCatalogApi'
import { migrationApi } from './modules/migrationApi'
import { teamImApi } from './modules/teamImApi'

export const api = {
  employeeTemplate: employeeTemplateApi,
  hiringWorkflow: hiringWorkflowApi,
  employeeRuntime: employeeRuntimeApi,
  collaboration: collaborationApi,
  skillCatalog: skillCatalogApi,
  migration: migrationApi,
  teamIm: teamImApi,
}

export { ApiClientError } from './httpClient'

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
  HiringDiagnosticStatus,
  HiringStageReadinessStatus,
  HiringCredentialBindingStatus,
} from './modules/hiringWorkflowApi'

export type {
  HiringCollectionPhaseType,
  HiringCollectionStageType,
  HiringAuditDecisionType,
  HiringTodoStatusType,
  HiringDiagnosticStatusType,
  HiringStageReadinessStatusType,
  HiringCredentialBindingStatusType,
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
  CredentialSlot,
  ConfigGovernanceFile,
  ConfigGovernanceState,
  HiringConversationMaterial,
  StartHiringConversationResult,
  HiringConversationMessageRequest,
  HiringConversationMessage,
  HiringStagePreview,
  HiringConversationResult,
  HiringConversationControlResult,
  HiringConversationTimeline,
  HiringAuditDecisionRequest,
  HiringAuditDecisionResult,
  HiringAuditLog,
  HiringFinalizeResult,
  HiringWorkflowState,
  HiringArtifactsDownloadData,
  HandoffItem,
} from './modules/hiringWorkflowApi'

export type {
  EmployeeCapability,
  EmployeeSummary,
  EmployeeDetail,
  ImPlatformId,
  ImConnectionMode,
  FeishuChannelEffectiveConfig,
  DingTalkChannelEffectiveConfig,
  DingTalkChannelConfigRequest,
  ImConfigRequest,
  ImWebhookUrl,
  ImConfigResult,
  ImConfigItem,
  ImConfigStatus,
  UpdateEmployeeLifecycleRequest,
  UpdateEmployeeCapabilitiesRequest,
  CreatePersonalCloneRequest,
  InstanceChatMessage,
  InstanceChatTimeline,
  InstanceChatResult,
  SendInstanceChatMessageRequest,
  TrainingCheckpoint,
  TrainingState,
  TrainingDecisionRequest,
  EvaluationScenario,
  EvaluationReadiness,
  EvaluationQuestionCard,
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
  ImportFixtureInstancesResult,
} from './modules/employeeRuntimeApi'

export type {
  CollaborationGroupSummary,
  CollaborationGroupMember,
  CollaborationGroupDetail,
  ArchiveCollaborationGroupRequest,
} from './modules/collaborationApi'

export type {
  SkillSummary,
  SkillDetail,
} from './modules/skillCatalogApi'

export type {
  LocalStateEmployeeMigrationItem,
  LocalStateMigrationRequest,
  LocalStateMigrationResult,
} from './modules/migrationApi'

export type {
  TeamImItem,
  TeamImQuery,
  ConfirmTeamImItemRequest,
} from './modules/teamImApi'
