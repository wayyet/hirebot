import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Lock } from "lucide-react";
import { signOut } from "@/infra/auth/oidc";

export default function ForbiddenBlankPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const [logoutLoading, setLogoutLoading] = useState(false);

  useEffect(() => {
    document.title = `${t("brand.name")} · ${t("auth.forbidden.title")}`;
  }, [i18n.language, t]);

  async function handleLogout() {
    if (logoutLoading) return;
    setLogoutLoading(true);
    try {
      await signOut();
    } catch (error) {
      console.warn("Logout failed on forbidden page:", error);
      setLogoutLoading(false);
      navigate("/", { replace: true });
    }
  }

  return (
    <div className="min-h-screen bg-slate-50 flex items-center justify-center px-6 py-10" aria-label="403 forbidden">
      <div className="w-full max-w-[560px] rounded-3xl border border-slate-200/80 bg-white/85 p-10 text-center shadow-[0_24px_80px_rgba(15,23,42,0.12)] backdrop-blur-xl">
        <div className="mx-auto mb-5 grid h-[72px] w-[72px] place-items-center rounded-2xl bg-blue-50 text-blue-700">
          <Lock size={32} />
        </div>

        <h1 className="text-2xl font-semibold text-slate-900">{t("auth.forbidden.title")}</h1>

        <div className="mt-7 flex items-center justify-center gap-3">
            <button
              type="button"
              className="px-5 py-2.5 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 transition-colors"
              onClick={() => navigate(-1)}
            >
              {t("common.back")}
            </button>

            <button
              type="button"
              className="px-4 py-2 border border-slate-200 text-slate-600 rounded-lg text-sm hover:bg-slate-50 transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
              onClick={() => void handleLogout()}
              disabled={logoutLoading}
            >
              {logoutLoading ? t("common.loading") : t("user.logout")}
            </button>
          </div>
      </div>
    </div>
  );
}
