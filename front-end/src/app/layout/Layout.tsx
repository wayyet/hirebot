import { useEffect, useMemo, useRef, useState } from "react";
import {
  ChevronDown,
  Globe,
  Home,
  Loader2,
  LogOut,
  Moon,
  Palette,
  Settings,
  Sun,
  User,
} from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import i18n from "@/i18n";
import yWorkHireLogo from "@/assets/y-work-hire-logo.svg";
import {
  resolveBrandWordmarkSrc,
  resolveDisplayProductName,
  resolveSystemTitle,
} from "@/app/branding/runtimeBranding";
import {
  UserRoleContext,
  type HirebotUserRole,
} from "@/app/context/UserRoleContext";
import { useTheme } from "@/app/theme/ThemeProvider";
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
    return pathname.startsWith("/template-pool/") && pathname !== "/template-pool";
  }

  if (navPath === "/department-employees") {
    return (
      !pathname.startsWith("/my-employees") &&
      (pathname.startsWith("/department-employees") ||
        pathname.includes("/evaluation") ||
        pathname.includes("/review") ||
        pathname.includes("/onboarding"))
    );
  }

  if (navPath === "/my-employees") {
    return (
      pathname.startsWith("/my-employees") ||
      pathname.startsWith("/clone/") ||
      pathname.startsWith("/private-branch/") ||
      (!pathname.startsWith("/department-employees") && pathname.includes("/chat"))
    );
  }

  return pathname.startsWith(navPath);
}

export default function Layout({ children }: { children: React.ReactNode }) {
  const location = useLocation();
  const navigate = useNavigate();
  const { showToast } = useUxOverlay();
  const { t } = useTranslation();
  const { brand, cycleBrand, isDark, toggleMode, warmThemeEnabled, warmThemeManagedByRuntime } = useTheme();
  const currentLang = i18n.resolvedLanguage ?? i18n.language ?? "zh";
  const brandName = resolveDisplayProductName(warmThemeEnabled, t("brand.name"));
  const originalBrandWordmarkSrc = resolveBrandWordmarkSrc(currentLang);
  const [role, setRole] = useState<HirebotUserRole>(deriveDefaultRole);
  const [logoutLoading, setLogoutLoading] = useState(false);
  const { data: authUser, isLoading: loadingUser } = useQuery({
    queryKey: ["auth-user"],
    queryFn: getAuthUser,
    staleTime: 60_000,
    retry: false,
    enabled: !isAuthBypassed,
  });

  // 响应式计算显示名：语言切换时自动更新
  const userDisplayName = useMemo(
    () => (authUser ? getUserDisplayName(authUser, currentLang) : ''),
    [authUser, currentLang],
  );

  const [navCollapsed, setNavCollapsed] = useState(false);
  const [navStacked, setNavStacked] = useState(false);
  const [navMenuOpen, setNavMenuOpen] = useState(false);
  const layoutRef = useRef<HTMLDivElement | null>(null);
  const brandRef = useRef<HTMLDivElement | null>(null);
  const navMeasureRef = useRef<HTMLDivElement | null>(null);
  const actionsRef = useRef<HTMLDivElement | null>(null);

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
      .filter((element): element is HTMLDivElement => Boolean(element));
    observedElements.forEach((element) => resizeObserver?.observe(element));

    return () => {
      cancelAnimationFrame(rafId);
      window.removeEventListener("resize", scheduleMeasure);
      resizeObserver?.disconnect();
    };
  }, [role, t]);

  useEffect(() => {
    document.title = resolveSystemTitle(warmThemeEnabled, currentLang);
  }, [currentLang, warmThemeEnabled]);

  const [langOpen, setLangOpen] = useState(false);
  const langRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleOutside(event: MouseEvent) {
      if (langRef.current && !langRef.current.contains(event.target as Node)) {
        setLangOpen(false);
      }
    }

    if (langOpen) {
      document.addEventListener("mousedown", handleOutside);
    }

    return () => document.removeEventListener("mousedown", handleOutside);
  }, [langOpen]);

  const [userOpen, setUserOpen] = useState(false);
  const userRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleOutside(event: MouseEvent) {
      if (userRef.current && !userRef.current.contains(event.target as Node)) {
        setUserOpen(false);
      }
    }

    if (userOpen) {
      document.addEventListener("mousedown", handleOutside);
    }

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
  const userEmail = typeof authUser?.profile.email === "string" ? authUser.profile.email : "";

  return (
    <UserRoleContext.Provider value={{ role, setRole }}>
      <div className="hb-shell">
        <header className="hb-topnav">
          <div ref={layoutRef} className={`hb-topnav-inner${navStacked ? " is-stacked" : ""}`}>
            <div ref={brandRef} style={{ flexShrink: 0 }}>
              <Link
                to="/"
                className="hb-brand"
                title={t("common.backHome")}
                aria-label={t("common.backHome")}
              >
                {warmThemeEnabled ? (
                  <>
                    <div className="hb-brand-logo hb-brand-logo--mark">
                      <img src={yWorkHireLogo} alt="" className="hb-brand-logo-mark" />
                    </div>
                    <div className="hb-brand-body">
                      <span className="hb-brand-name">{brandName}</span>
                      <span className="hb-brand-tagline">{t("brand.tagline")}</span>
                    </div>
                  </>
                ) : (
                  <img
                    src={originalBrandWordmarkSrc}
                    alt={t("brand.name")}
                    className="hb-brand-wordmark"
                  />
                )}
              </Link>
            </div>

            <div className={`hb-topnav-center${navStacked && navCollapsed ? " is-stacked" : ""}`}>
              <div ref={navMeasureRef} aria-hidden="true" className="hb-nav-measure">
                <div className="hb-nav-pill-shell">
                  {visibleNavItems.map((item) => (
                    <span
                      key={`measure-${item.path}`}
                      className="hb-nav-pill-item"
                      style={{ background: "transparent", boxShadow: "none" }}
                    >
                      {t(item.labelKey)}
                    </span>
                  ))}
                </div>
              </div>

              {!navCollapsed ? (
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
                        {active ? <span className="hb-nav-pill-dot" /> : null}
                      </Link>
                    );
                  })}
                </nav>
              ) : (
                <div style={{ position: "relative", width: navStacked ? "100%" : "auto" }}>
                  <button
                    type="button"
                    className={`hb-nav-collapse-btn${navStacked ? " is-stacked" : ""}`}
                    onClick={() => {
                      setNavMenuOpen((current) => !current);
                      setLangOpen(false);
                      setUserOpen(false);
                    }}
                  >
                    <span className="hb-nav-collapse-btn__label">
                      {t(
                        visibleNavItems.find((item) => isNavItemActive(location.pathname, item.path))
                          ?.labelKey ?? visibleNavItems[0]?.labelKey ?? "",
                      )}
                    </span>
                    <ChevronDown
                      size={13}
                      style={{
                        transform: navMenuOpen ? "rotate(180deg)" : "none",
                        transition: "transform 180ms ease",
                        flexShrink: 0,
                      }}
                    />
                  </button>

                  {navMenuOpen ? (
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
                  ) : null}
                </div>
              )}
            </div>

            <div ref={actionsRef} className="hb-nav-actions">
              {!warmThemeEnabled ? (
                <button
                  type="button"
                  className="hb-icon-btn"
                  title={t("theme.toggle")}
                  onClick={toggleMode}
                >
                  {isDark ? <Sun size={15} /> : <Moon size={15} />}
                </button>
              ) : null}

              {!warmThemeManagedByRuntime ? (
                <button
                  type="button"
                  className="hb-nav-utility-btn"
                  title={t("theme.brandToggle")}
                  onClick={cycleBrand}
                >
                  <Palette size={14} />
                  <span>{t(`theme.brand.${brand}`)}</span>
                </button>
              ) : null}

              <div className="hb-lang-dropdown" ref={langRef}>
                <button
                  type="button"
                  className="hb-nav-utility-btn"
                  title={t("language.toggle")}
                  onClick={() => {
                    setLangOpen((current) => !current);
                    setUserOpen(false);
                  }}
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
                {langOpen ? (
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
                ) : null}
              </div>

              <div className="landing-auth-popover" ref={userRef}>
                <button
                  type="button"
                  className="app-layout-user-button"
                  onClick={() => {
                    setUserOpen((current) => !current);
                    setLangOpen(false);
                    setNavMenuOpen(false);
                  }}
                  aria-haspopup="menu"
                  aria-expanded={userOpen}
                >
                  <div className="app-layout-user-avatar">
                    <User size={12} />
                  </div>
                  <span className="app-layout-user-name">{displayName}</span>
                  <ChevronDown
                    size={12}
                    className={`app-layout-chevron${userOpen ? " is-open" : ""}`}
                  />
                </button>
                {userOpen ? (
                  <div className="glass-modal app-layout-menu app-layout-user-menu" role="menu">
                    <div className="app-layout-user-menu-header">
                      <div className="app-layout-user-menu-avatar">
                        <User size={15} />
                      </div>
                      <div className="app-layout-user-menu-meta">
                        <div className="app-layout-user-menu-name">{displayName}</div>
                        {userEmail ? (
                          <div className="app-layout-user-menu-email">{userEmail}</div>
                        ) : null}
                      </div>
                    </div>
                    <button
                      type="button"
                      className="app-layout-menu-item"
                      onClick={() => {
                        setUserOpen(false);
                        navigate("/");
                      }}
                    >
                      <span className="app-layout-menu-icon">
                        <Home size={14} />
                      </span>
                      <span>{t("common.backHome")}</span>
                    </button>
                    <button
                      type="button"
                      className="app-layout-menu-item"
                      onClick={() => {
                        setUserOpen(false);
                        navigate("/settings");
                      }}
                    >
                      <span className="app-layout-menu-icon">
                        <Settings size={14} />
                      </span>
                      <span>{t("user.settings")}</span>
                    </button>
                    {!isAuthBypassed ? (
                      <>
                        <div className="app-layout-menu-divider" />
                        <button
                          type="button"
                          className="app-layout-menu-item is-danger"
                          disabled={logoutLoading}
                          onClick={() => {
                            setUserOpen(false);
                            void handleLogout();
                          }}
                        >
                          <span className="app-layout-menu-icon">
                            {logoutLoading ? (
                              <Loader2 size={14} className="animate-spin" />
                            ) : (
                              <LogOut size={14} />
                            )}
                          </span>
                          <span>{t("nav.logout")}</span>
                        </button>
                      </>
                    ) : null}
                  </div>
                ) : null}
              </div>
            </div>
          </div>
        </header>
        <main className="hb-main">{children}</main>
      </div>
    </UserRoleContext.Provider>
  );
}
