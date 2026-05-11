import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronDown, Globe, Loader2, LogOut, Moon, Sparkles, Sun } from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import i18n from "@/i18n";
import {
  UserRoleContext,
  type HirebotUserRole,
} from "@/app/context/UserRoleContext";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import { isAuthBypassed } from "@/infra/auth/auth-mode";
import { signOut, getAuthUser, getUserDisplayName } from "@/infra/auth/oidc";

const ROLE_STORAGE_KEY = "hirebot_user_role_v1";

type NavItem = {
  path: string;
  labelKey: string;
  managerOnly?: boolean;
  alwaysVisible?: boolean;
  isNew?: boolean;
};

const navItems: NavItem[] = [
  {
    path: "/template-pool",
    labelKey: "nav.templatePool",
    managerOnly: true,
  },
  { path: "/department-employees", labelKey: "nav.departmentEmployees", alwaysVisible: true },
  { path: "/my-employees", labelKey: "nav.myEmployees", alwaysVisible: true },
];

function deriveDefaultRole(): HirebotUserRole {
  const cachedRole = localStorage.getItem(ROLE_STORAGE_KEY);
  if (cachedRole === "manager" || cachedRole === "member") {
    return cachedRole;
  }
  return "manager";
}

function isNavItemActive(pathname: string, navPath: string) {
  if (pathname === navPath) return true;
  if (navPath === "/template-pool") {
    return (
      pathname.startsWith("/templates/") || pathname.startsWith("/hiring/")
    );
  }
  if (navPath === "/department-employees") {
    return (
      pathname.startsWith("/department-employees") ||
      pathname.startsWith("/instances/") ||
      pathname.includes("/evaluation") ||
      pathname.includes("/review") ||
      pathname.includes("/onboarding")
    );
  }
  if (navPath === "/my-employees") {
    return (
      pathname.startsWith("/my-employees") ||
      pathname.startsWith("/clone/") ||
      pathname.startsWith("/private-branch/") ||
      pathname.includes("/chat")
    );
  }

  return pathname.startsWith(navPath);
}

export default function Layout({ children }: { children: React.ReactNode }) {
  const location = useLocation();
  const navigate = useNavigate();
  const { showToast } = useUxOverlay();
  const { t } = useTranslation();
  const [role, setRole] = useState<HirebotUserRole>(deriveDefaultRole);
  const [logoutLoading, setLogoutLoading] = useState(false);
  const [userDisplayName, setUserDisplayName] = useState<string>("");
  const [loadingUser, setLoadingUser] = useState(true);

  // 自适应导航折叠
  const [navCollapsed, setNavCollapsed] = useState(false);
  const [navStacked, setNavStacked] = useState(false);
  const [navMenuOpen, setNavMenuOpen] = useState(false);
  const layoutRef = useRef<HTMLDivElement | null>(null);
  const brandRef = useRef<HTMLDivElement | null>(null);
  const navMeasureRef = useRef<HTMLDivElement | null>(null);
  const actionsRef = useRef<HTMLDivElement | null>(null);

  // Dark / light theme
  const [isDark, setIsDark] = useState(
    () => localStorage.getItem("ncrew-hire-theme") === "dark",
  );

  useEffect(() => {
    document.documentElement.classList.toggle("dark", isDark);
    localStorage.setItem("ncrew-hire-theme", isDark ? "dark" : "light");
  }, [isDark]);

  // 自适应导航：通过 ResizeObserver 测量各区域宽度，决定是否折叠导航
  useEffect(() => {
    let rafId = 0;
    const measureNavLayout = () => {
      const layoutWidth = layoutRef.current?.clientWidth ?? 0;
      const brandWidth = brandRef.current?.offsetWidth ?? 0;
      const navWidth = navMeasureRef.current?.scrollWidth ?? 0;
      const actionsWidth = actionsRef.current?.offsetWidth ?? 0;
      if (!layoutWidth || !brandWidth || !navWidth || !actionsWidth) return;

      const requiredWidth = brandWidth + navWidth + actionsWidth + 48;
      setNavCollapsed(requiredWidth > layoutWidth);
      setNavStacked(layoutWidth < 520);
    };
    const scheduleMeasure = () => {
      cancelAnimationFrame(rafId);
      rafId = requestAnimationFrame(measureNavLayout);
    };

    scheduleMeasure();
    window.addEventListener("resize", scheduleMeasure);

    const resizeObserver = typeof ResizeObserver !== "undefined"
      ? new ResizeObserver(() => scheduleMeasure())
      : null;

    const observedElements = [layoutRef.current, brandRef.current, navMeasureRef.current, actionsRef.current]
      .filter((el): el is HTMLDivElement => Boolean(el));
    observedElements.forEach(el => resizeObserver?.observe(el));

    return () => {
      cancelAnimationFrame(rafId);
      window.removeEventListener("resize", scheduleMeasure);
      resizeObserver?.disconnect();
    };
  }, [t, role]);

  // Language switcher
  const [langOpen, setLangOpen] = useState(false);
  const langRef = useRef<HTMLDivElement>(null);
  const currentLang = i18n.language ?? "zh";

  useEffect(() => {
    function handleOutside(e: MouseEvent) {
      if (langRef.current && !langRef.current.contains(e.target as Node)) {
        setLangOpen(false);
      }
    }
    if (langOpen) document.addEventListener("mousedown", handleOutside);
    return () => document.removeEventListener("mousedown", handleOutside);
  }, [langOpen]);

  // User dropdown
  const [userOpen, setUserOpen] = useState(false);
  const userRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleOutside(e: MouseEvent) {
      if (userRef.current && !userRef.current.contains(e.target as Node)) {
        setUserOpen(false);
      }
    }
    if (userOpen) document.addEventListener("mousedown", handleOutside);
    return () => document.removeEventListener("mousedown", handleOutside);
  }, [userOpen]);

  async function switchLang(lang: string) {
    await i18n.changeLanguage(lang);
    localStorage.setItem("ncrew-hire-lang", lang);
    setLangOpen(false);
  }

  useEffect(() => {
    localStorage.setItem(ROLE_STORAGE_KEY, role);
  }, [role]);

  useEffect(() => {
    if (role === "member" && location.pathname.startsWith("/template-pool")) {
      navigate("/department-employees", { replace: true });
    }
  }, [location.pathname, navigate, role]);

  useEffect(() => {
    async function loadUserInfo() {
      try {
        const user = await getAuthUser();
        if (user) {
          setUserDisplayName(getUserDisplayName(user));
        }
      } catch (err) {
        console.warn("Failed to load user info:", err);
      } finally {
        setLoadingUser(false);
      }
    }
    loadUserInfo();
  }, []);

  const visibleNavItems = useMemo(() => {
    return navItems.filter((item) => {
      if (item.alwaysVisible) return true;
      if (item.managerOnly) return role === "manager";
      return true;
    });
  }, [role]);

  async function handleLogout() {
    if (logoutLoading) return;
    setLogoutLoading(true);
    try {
      await signOut();
    } catch (logoutError: unknown) {
      setLogoutLoading(false);
      showToast(
        logoutError instanceof Error ? logoutError.message : t("user.logoutFailed"),
        "error",
      );
    }
  }

  const displayName = loadingUser ? t("user.loading") : userDisplayName || t("user.defaultName");
  const avatarLetter = loadingUser ? "?" : (userDisplayName?.charAt(0)?.toUpperCase() ?? "?");

  return (
    <UserRoleContext.Provider value={{ role, setRole }}>
      <div className="hb-shell">
        <header className="hb-topnav">
          <div ref={layoutRef} className={`hb-topnav-inner${navStacked ? " is-stacked" : ""}`}>

            {/* ── Brand ── */}
            <div ref={brandRef} style={{ flexShrink: 0 }}>
              <Link
                to={role === "manager" ? "/template-pool" : "/department-employees"}
                className="hb-brand"
              >
                <div className="hb-brand-logo">
                  <Sparkles size={16} color="#fff" />
                </div>
                <div className="hb-brand-body">
                  <span className="hb-brand-name">{t("brand.name")}</span>
                  <span className="hb-brand-tagline">{t("brand.tagline")}</span>
                </div>
              </Link>
            </div>

            {/* ── Nav center（居中，自适应折叠） ── */}
            <div className={`hb-topnav-center${navStacked && navCollapsed ? " is-stacked" : ""}`}>
              {/* 用于测量导航实际宽度的隐藏克隆 */}
              <div ref={navMeasureRef} aria-hidden="true" className="hb-nav-measure">
                <div className="hb-nav-pill-shell">
                  {visibleNavItems.map((item) => (
                    <span key={`measure-${item.path}`} className="hb-nav-pill-item" style={{ background: "transparent", boxShadow: "none" }}>
                      {t(item.labelKey)}
                    </span>
                  ))}
                </div>
              </div>

              {!navCollapsed ? (
                /* 正常宽度：展示 pill 导航 */
                <nav className="hb-nav-pill-shell">
                  {visibleNavItems.map((item) => {
                    const active = isNavItemActive(location.pathname, item.path);
                    return (
                      <Link
                        key={item.path}
                        to={item.path}
                        className={`hb-nav-pill-item${active ? " is-active" : ""}`}
                      >
                        {t(item.labelKey)}
                        {item.isNew ? <span className="hb-nav-flag">new</span> : null}
                        {active && <span className="hb-nav-pill-dot" />}
                      </Link>
                    );
                  })}
                </nav>
              ) : (
                /* 宽度不足：折叠为下拉菜单 */
                <div style={{ position: "relative", width: navStacked ? "100%" : "auto" }}>
                  <button
                    type="button"
                    className={`hb-nav-collapse-btn${navStacked ? " is-stacked" : ""}`}
                    onClick={() => { setNavMenuOpen(v => !v); setLangOpen(false); setUserOpen(false); }}
                  >
                    <span className="hb-nav-collapse-btn__label">
                      {t(visibleNavItems.find(item => isNavItemActive(location.pathname, item.path))?.labelKey ?? visibleNavItems[0]?.labelKey ?? "")}
                    </span>
                    <ChevronDown
                      size={13}
                      style={{ transform: navMenuOpen ? "rotate(180deg)" : "none", transition: "transform 180ms ease", flexShrink: 0 }}
                    />
                  </button>

                  {navMenuOpen && (
                    <div className={`hb-nav-collapse-menu${navStacked ? " is-stacked" : ""}`}>
                      {visibleNavItems.map((item) => {
                        const active = isNavItemActive(location.pathname, item.path);
                        return (
                          <Link
                            key={item.path}
                            to={item.path}
                            className={`hb-nav-menu-item${active ? " is-active" : ""}`}
                            onClick={() => setNavMenuOpen(false)}
                          >
                            {t(item.labelKey)}
                          </Link>
                        );
                      })}
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* ── Right actions ── */}
            <div ref={actionsRef} className="hb-nav-actions">

              {/* Theme toggle */}
              <button
                type="button"
                className="hb-icon-btn"
                title={t("theme.toggle")}
                onClick={() => setIsDark((prev) => !prev)}
              >
                {isDark ? <Sun size={15} /> : <Moon size={15} />}
              </button>

              {/* Language switcher */}
              <div className="hb-lang-dropdown" ref={langRef}>
                <button
                  type="button"
                  className="hb-nav-utility-btn"
                  title={t("language.toggle")}
                  onClick={() => { setLangOpen((v) => !v); setUserOpen(false); }}
                >
                  <Globe size={14} />
                  <span>{currentLang === "zh" ? "中文" : "EN"}</span>
                  <ChevronDown
                    size={12}
                    style={{
                      transform: langOpen ? "rotate(180deg)" : "none",
                      transition: "transform 180ms ease",
                    }}
                  />
                </button>
                {langOpen && (
                  <div className="hb-dropdown-menu">
                    {(["zh", "en"] as const).map((code) => (
                      <button
                        key={code}
                        type="button"
                        className={`hb-dropdown-item${currentLang === code ? " is-active" : ""}`}
                        onClick={() => void switchLang(code)}
                      >
                        {t(`language.${code}`)}
                      </button>
                    ))}
                  </div>
                )}
              </div>

              {/* User dropdown */}
              <div className="hb-user-dropdown" ref={userRef}>
                <button
                  type="button"
                  className="hb-user-btn"
                  onClick={() => { setUserOpen((v) => !v); setLangOpen(false); }}
                >
                  <div className="hb-user-avatar">{avatarLetter}</div>
                  <span className="hb-user-name">{displayName}</span>
                  <ChevronDown
                    size={12}
                    style={{
                      transform: userOpen ? "rotate(180deg)" : "none",
                      transition: "transform 180ms ease",
                      flexShrink: 0,
                    }}
                  />
                </button>
                {userOpen && (
                  <div className="hb-dropdown-menu hb-dropdown-menu--right">
                    {!isAuthBypassed && (
                      <button
                        type="button"
                        className="hb-dropdown-item hb-dropdown-item--danger"
                        disabled={logoutLoading}
                        onClick={() => { setUserOpen(false); void handleLogout(); }}
                      >
                        {logoutLoading
                          ? <Loader2 size={13} className="animate-spin" />
                          : <LogOut size={13} />
                        }
                        {t("user.logout")}
                      </button>
                    )}
                  </div>
                )}
              </div>

            </div>
          </div>
        </header>
        <main className="hb-main">{children}</main>
        <button
          type="button"
          className="hb-feedback-strip"
          onClick={() => showToast(t("feedback.sent"), "info")}
        >
          {t("nav.feedback")}
        </button>
      </div>
    </UserRoleContext.Provider>
  );
}
