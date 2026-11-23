import { useEffect, useMemo, useState, type ReactElement } from "react";
import { api, ApiError, type Me } from "./api";
import { authMode, isSignedIn, signIn, signInDev, signOut } from "./auth";
import { I18nContext, makeI18n, type Lang } from "./i18n";
import { Loading } from "./ui";
import Home from "./pages/Home";
import Projects from "./pages/Projects";
import ProjectDetail from "./pages/ProjectDetail";
import Leadership from "./pages/Leadership";
import Kpis from "./pages/Kpis";
import Approvals from "./pages/Approvals";
import Changes from "./pages/Changes";
import Reports from "./pages/Reports";
import Admin from "./pages/Admin";

/** Menu order. Each user only sees the pages their role allows (the API sends that list). */
const TABS = ["home", "leadership", "kpis", "projects", "approvals", "changes", "reports", "users"] as const;

/** Demo users for sign-in in development (database/seed/people.example.json). */
const DEMO_USERS: [string, string][] = [
  ["pmo@example.org", "PMO"],
  ["executive@example.org", "Executive"],
  ["lead.attiyah@example.org", "Sector lead · Maha Al Attiyah"],
  ["lead.khayarin@example.org", "Sector lead · Fahad Al Khayarin"],
  ["lead.minister@example.org", "Sector lead · Minister's Sector"],
  ["pm.demo@example.org", "Project manager"],
];

function useHash(): [string, (h: string) => void] {
  const [hash, setHash] = useState(() => window.location.hash.slice(1) || "home");
  useEffect(() => {
    const on = () => setHash(window.location.hash.slice(1) || "home");
    window.addEventListener("hashchange", on);
    return () => window.removeEventListener("hashchange", on);
  }, []);
  return [hash, (h) => (window.location.hash = h)];
}

export default function App() {
  const [lang, setLang] = useState<Lang>(() => (localStorage.getItem("mkash.lang") as Lang) || "en");
  const i18n = useMemo(() => makeI18n(lang, (l) => { localStorage.setItem("mkash.lang", l); setLang(l); }), [lang]);
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [signedIn, setSignedIn] = useState(isSignedIn());
  const [hash, go] = useHash();

  useEffect(() => {
    document.documentElement.lang = lang;
    document.documentElement.dir = lang === "ar" ? "rtl" : "ltr";
  }, [lang]);

  useEffect(() => {
    if (!signedIn) return;
    setError(null);
    api
      .get<Me>("/me")
      .then(setMe)
      .catch((e: ApiError) => setError(e.status === 403 ? i18n.t("notRegistered") : e.message));
  }, [signedIn, i18n]);

  const t = i18n.t;

  if (!signedIn) {
    return (
      <I18nContext.Provider value={i18n}>
        <div className="login">
          <div className="card">
            <h1>{t("brand")}</h1>
            <p className="muted">{t("tagline")}</p>
            {authMode === "entra" ? (
              <button className="btn primary mt" onClick={() => void signIn()}>
                {t("signInEntra")}
              </button>
            ) : (
              <>
                <p className="muted mt">{t("signInDev")}</p>
                <form
                  className="row mt"
                  onSubmit={(e) => {
                    e.preventDefault();
                    const v = new FormData(e.currentTarget).get("email")?.toString().trim();
                    if (v) {
                      signInDev(v.toLowerCase());
                      setSignedIn(true);
                    }
                  }}
                >
                  <input className="field" name="email" type="email" placeholder="you@example.com" style={{ flex: 1 }} dir="ltr" />
                  <button className="btn primary">{t("signIn")}</button>
                </form>
                <div className="users">
                  {DEMO_USERS.map(([email, label]) => (
                    <button key={email} className="btn" onClick={() => { signInDev(email); setSignedIn(true); }}>
                      <span>{label}</span>
                      <span className="muted">{email}</span>
                    </button>
                  ))}
                </div>
              </>
            )}
            <button className="btn mt" onClick={() => i18n.setLang(lang === "ar" ? "en" : "ar")}>
              {t("language")}
            </button>
          </div>
        </div>
      </I18nContext.Provider>
    );
  }

  const out = async () => {
    await signOut();
    setMe(null);
    setSignedIn(false);
    go("home");
  };

  if (!me)
    return (
      <I18nContext.Provider value={i18n}>
        <main>
          <Loading error={error} />
          {error && (
            <button className="btn mt" onClick={() => void out()}>
              {t("signOut")}
            </button>
          )}
        </main>
      </I18nContext.Provider>
    );

  const [page, ...rest] = hash.split("/");
  const allowed = (p: string) => me.pages.includes(p);
  const current = allowed(page) ? page : "home";

  let body: ReactElement;
  switch (current) {
    case "projects":
      body = rest[0] ? <ProjectDetail id={Number(rest[0])} me={me} /> : <Projects me={me} />;
      break;
    case "leadership":
      body = <Leadership />;
      break;
    case "kpis":
      body = <Kpis />;
      break;
    case "approvals":
      body = <Approvals />;
      break;
    case "changes":
      body = <Changes me={me} id={rest[0] ? Number(rest[0]) : undefined} />;
      break;
    case "reports":
      body = <Reports />;
      break;
    case "users":
      body = <Admin />;
      break;
    default:
      body = <Home />;
  }

  return (
    <I18nContext.Provider value={i18n}>
      <header className="top">
        <div className="top-row">
          <div className="brand">
            <i /> {t("brand")}
          </div>
          <div className="who">
            <span>
              {i18n.pick(me.nameAr, me.nameEn)} · {t(me.role)}
            </span>
            <button className="ghost" onClick={() => i18n.setLang(lang === "ar" ? "en" : "ar")}>
              {t("language")}
            </button>
            <button className="ghost" onClick={() => void out()}>
              {authMode === "dev" ? t("switchUser") : t("signOut")}
            </button>
          </div>
        </div>
        <nav className="tabs">
          {TABS.filter((p) => me.pages.includes(p)).map((p) => (
            <a key={p} href={`#${p}`} className={current === p ? "on" : ""}>
              {t(p)}
            </a>
          ))}
        </nav>
      </header>
      <main>{body}</main>
    </I18nContext.Provider>
  );
}
