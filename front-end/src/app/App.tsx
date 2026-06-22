import { useEffect, type ReactNode } from "react";
import { BrowserRouter, Routes, Route, Navigate } from "react-router-dom";
import Layout from "@/app/layout/Layout";
import AuthGate from "@/app/guards/AuthGate";
import { UxOverlayProvider } from "@/app/context/UxOverlayContext";
import { api } from "@/infra/api";
import type { LocalStateMigrationRequest } from "@/infra/api";
import LandingPage from "@/features/auth/pages/LandingPage";
import AuthCallbackPage from "@/features/auth/pages/AuthCallbackPage";
import ForbiddenBlankPage from "@/features/auth/pages/ForbiddenBlankPage";
import MarketPage from "@/features/market/pages/MarketPage";
import TemplateDetailPage from "@/features/market/pages/TemplateDetailPage";
import DepartmentEmployeesPage from "@/features/hiring/pages/DepartmentEmployeesPage";
import HiringPage from "@/features/hiring/pages/HiringPage";
import MyEmployeesPage from "@/features/hiring/pages/MyEmployeesPage";
import ReviewPage from "@/features/hiring/pages/ReviewPage";
import OnboardingPage from "@/features/hiring/pages/OnboardingPage";
import CloneEmployeePage from "@/features/hiring/pages/CloneEmployeePage";
import PrivateBranchPage from "@/features/hiring/pages/PrivateBranchPage";
import InstanceDetailPage from "@/features/team/pages/InstanceDetailPage";
import InstanceChatPage from "@/features/team/pages/InstanceChatPage";
import InstanceImConfigPage from "@/features/team/pages/InstanceImConfigPage";
import EvaluationPage from "@/features/team/pages/EvaluationPage";
import HumanEvaluationPage from "@/features/team/pages/HumanEvaluationPage";
import SettingsPage from "@/features/hiring/pages/SettingsPage";

const LOCAL_STATE_MIGRATION_FLAG = "ncrew_local_state_migrated_v1";
const EMPLOYEE_KEY = "ncrew_digital_employees";
const ARCHIVED_GROUPS_KEY = "ncrew_archived_groups";

function tryBuildMigrationRequest(): LocalStateMigrationRequest | null {
  try {
    const employeeRaw = localStorage.getItem(EMPLOYEE_KEY);
    const archivedRaw = localStorage.getItem(ARCHIVED_GROUPS_KEY);

    const employees = employeeRaw ? JSON.parse(employeeRaw) : [];
    const archivedGroupIds = archivedRaw ? JSON.parse(archivedRaw) : [];

    if (!Array.isArray(employees) && !Array.isArray(archivedGroupIds)) {
      return null;
    }

    const mappedEmployees = Array.isArray(employees)
      ? employees
          .filter((item) => item && typeof item.id === "string")
          .map((item) => ({
            employeeId: item.id,
            nickname: item.nickname ?? "",
            roleName: item.roleName ?? "",
            sourceTemplate: item.sourceTemplate ?? "",
            sourceTemplateId: item.sourceTemplateId ?? "",
            lifecycleStatus: item.lifecycleStatus ?? "待启动",
            stageSummary: item.stageSummary ?? "",
            primarySignal: item.primarySignal ?? "",
            signalLevel: item.signalLevel ?? "ok",
            owningTeam: item.owningTeam ?? "默认团队",
            createdAt: item.createdAt ?? new Date().toISOString().split("T")[0],
            internshipStartAt: item.internshipStartAt,
            graduatedAt: item.graduatedAt,
            tasksDone: Number(item.tasksDone ?? 0),
            tasksTotal: Number(item.tasksTotal ?? 0),
            pendingActions: Array.isArray(item.pendingActions)
              ? item.pendingActions
              : [],
            capabilityNames: Array.isArray(item.capabilities)
              ? item.capabilities
                  .map((cap: { name?: unknown }) =>
                    typeof cap?.name === "string" ? cap.name : "",
                  )
                  .filter((name: string) => name.length > 0)
              : [],
            isConfigured: Boolean(item.isConfigured ?? true),
          }))
      : [];

    const mappedArchived = Array.isArray(archivedGroupIds)
      ? archivedGroupIds.filter((item) => typeof item === "string")
      : [];

    if (mappedEmployees.length === 0 && mappedArchived.length === 0) {
      return null;
    }

    return {
      employees: mappedEmployees,
      archivedGroupIds: mappedArchived,
    };
  } catch {
    return null;
  }
}

function LocalStateMigrationBootstrap() {
  useEffect(() => {
    if (localStorage.getItem(LOCAL_STATE_MIGRATION_FLAG) === "1") {
      return;
    }

    const payload = tryBuildMigrationRequest();
    if (!payload) {
      localStorage.setItem(LOCAL_STATE_MIGRATION_FLAG, "1");
      return;
    }

    api.migration
      .migrateLocalState(payload)
      .then(() => {
        localStorage.setItem(LOCAL_STATE_MIGRATION_FLAG, "1");
      })
      .catch(() => {
        // 保持静默，避免阻断主流程
      });
  }, []);

  return null;
}

function ProtectedLayout({ children }: { children: ReactNode }) {
  return (
    <AuthGate>
      <>
        <LocalStateMigrationBootstrap />
        <Layout>{children}</Layout>
      </>
    </AuthGate>
  );
}

export default function App() {
  return (
    <UxOverlayProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/" element={<LandingPage />} />
          <Route path="/login" element={<LandingPage />} />
          <Route path="/auth/callback" element={<AuthCallbackPage />} />
          <Route path="/403" element={<ForbiddenBlankPage />} />
          <Route
            path="/template-pool"
            element={
              <ProtectedLayout>
                <MarketPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/market"
            element={<Navigate to="/template-pool" replace />}
          />
          <Route
            path="/template-pool/templates/:id"
            element={
              <ProtectedLayout>
                <TemplateDetailPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/template-pool/hiring/:templateId"
            element={
              <ProtectedLayout>
                <HiringPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/department-employees"
            element={
              <ProtectedLayout>
                <DepartmentEmployeesPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/team"
            element={<Navigate to="/department-employees" replace />}
          />
          <Route
            path="/my-employees"
            element={
              <ProtectedLayout>
                <MyEmployeesPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/clone/:id"
            element={
              <ProtectedLayout>
                <CloneEmployeePage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/private-branch/:id"
            element={
              <ProtectedLayout>
                <PrivateBranchPage />
              </ProtectedLayout>
            }
          />
          {/* 我的数字员工下的实例页面 — 复用相同组件 */}
          <Route
            path="/my-employees/instances/:id"
            element={
              <ProtectedLayout>
                <InstanceDetailPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/my-employees/instances/:id/chat"
            element={
              <ProtectedLayout>
                <InstanceChatPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/my-employees/instances/:id/im-config"
            element={
              <ProtectedLayout>
                <InstanceImConfigPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/my-employees/instances/:id/evaluation"
            element={
              <ProtectedLayout>
                <EvaluationPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/my-employees/instances/:id/human-evaluation"
            element={
              <ProtectedLayout>
                <HumanEvaluationPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/my-employees/instances/:id/review"
            element={
              <ProtectedLayout>
                <ReviewPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/my-employees/instances/:id/onboarding"
            element={
              <ProtectedLayout>
                <OnboardingPage />
              </ProtectedLayout>
            }
          />

          {/* 部门数字员工下的实例页面 — 复用相同组件 */}
          <Route
            path="/department-employees/instances/:id"
            element={
              <ProtectedLayout>
                <InstanceDetailPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/department-employees/instances/:id/chat"
            element={
              <ProtectedLayout>
                <InstanceChatPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/department-employees/instances/:id/im-config"
            element={
              <ProtectedLayout>
                <InstanceImConfigPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/department-employees/instances/:id/evaluation"
            element={
              <ProtectedLayout>
                <EvaluationPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/department-employees/instances/:id/human-evaluation"
            element={
              <ProtectedLayout>
                <HumanEvaluationPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/department-employees/instances/:id/review"
            element={
              <ProtectedLayout>
                <ReviewPage />
              </ProtectedLayout>
            }
          />
          <Route
            path="/department-employees/instances/:id/onboarding"
            element={
              <ProtectedLayout>
                <OnboardingPage />
              </ProtectedLayout>
            }
          />

          <Route
            path="/settings"
            element={
              <ProtectedLayout>
                <SettingsPage />
              </ProtectedLayout>
            }
          />

          <Route path="*" element={<Navigate to="/template-pool" replace />} />
        </Routes>
      </BrowserRouter>
    </UxOverlayProvider>
  );
}
