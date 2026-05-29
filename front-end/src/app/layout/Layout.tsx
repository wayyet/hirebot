import { useEffect, useMemo, useRef, useState } from "react";
import {
  ChevronDown,
  Globe,
  Loader2,
  LogOut,
  Moon,
  Palette,
  Settings,
  Sun,
} from "lucide-react";
import { Link, useLocation, useNavigate } from "react-router-dom";
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
  const [userDisplayName, setUserDisplayName] = useState<string>("");
  const [loadingUser, setLoadingUser] = useState(true);

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

  useEffect(() => {
    async function loadUserInfo() {
      try {
        const user = await getAuthUser();
        if (user) {
          setUserDisplayName(getUserDisplayName(user));
        }
      } catch (error) {
        console.warn("Failed to load user info:", error);
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
            <div ref={brandRef} style={{ flexShrink: 0 }}>
              <Link
                to={role === "manager" ? "/template-pool" : "/department-employees"}
                className="hb-brand"
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

              <div className="hb-user-dropdown" ref={userRef}>
                <button
                  type="button"
                  className="hb-user-btn"
                  onClick={() => {
                    setUserOpen((current) => !current);
                    setLangOpen(false);
                  }}
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
                {userOpen ? (
                  <div className="hb-dropdown-menu hb-dropdown-menu--right">
                    <button
                      type="button"
                      className="hb-dropdown-item"
                      onClick={() => {
                        setUserOpen(false);
                        navigate("/settings");
                      }}
                    >
                      <Settings size={13} />
                      {t("user.settings")}
                    </button>
                    {!isAuthBypassed ? (
                      <button
                        type="button"
                        className="hb-dropdown-item hb-dropdown-item--danger"
                        disabled={logoutLoading}
                        onClick={() => {
                          setUserOpen(false);
                          void handleLogout();
                        }}
                      >
                        {logoutLoading
                          ? <Loader2 size={13} className="animate-spin" />
                          : <LogOut size={13} />}
                        {t("user.logout")}
                      </button>
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
