import { api, type Strategy } from "../api";
import { useI18n } from "../i18n";
import { Loading, useLoad } from "../ui";

/** Opening page for every user: the 2025–2030 strategy roadmap. */
export default function Home() {
  const i = useI18n();
  const { data, error, reload } = useLoad(() => api.get<Strategy>("/strategy"), []);
  if (!data) return <Loading error={error} retry={reload} />;
  const s = data;
  const bi = (x?: { ar: string; en: string }) => (x ? i.pick(x.ar, x.en) : "");
  return (
    <>
      <section className="hero">
        <div className="muted" style={{ color: "rgba(255,255,255,.75)" }}>
          2025 – 2030 {s.slogan && `· ${bi(s.slogan)}`}
        </div>
        <h1>{bi(s.vision)}</h1>
        <p>{bi(s.mission)}</p>
        <div className="tiles">
          <div className="tile">
            <b>{s.outcomes.length}</b>
            <span>{i.t("outcomes")}</span>
          </div>
          <div className="tile">
            <b>{s.outcomes.reduce((n, o) => n + o.initiatives.length, 0)}</b>
            <span>{i.t("initiatives")}</span>
          </div>
          <div className="tile">
            <b>{s.enablers.length}</b>
            <span>{i.t("enablers")}</span>
          </div>
        </div>
      </section>

      <h2 style={{ color: "var(--navy)", fontSize: 16, margin: "22px 0 10px" }}>{i.t("outcomes")}</h2>
      <div className="grid g3">
        {s.outcomes.map((o) => (
          <div key={o.number} className="outcome" style={{ background: o.color }}>
            <div className="n">
              {o.number} · {i.pick(o.pillarAr, o.pillarEn)}
            </div>
            <h3>{i.pick(o.nameAr, o.nameEn)}</h3>
            <ul>
              {o.initiatives.map((x) => (
                <li key={x.code}>
                  {x.code} {i.pick(x.nameAr, x.nameEn)}
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>

      <div className="card mt">
        <h2>{i.t("enablers")}</h2>
        <div className="row">
          {s.enablers.map((e, k) => (
            <span key={k} className="chip s-draft">
              {bi(e)}
            </span>
          ))}
        </div>
      </div>
    </>
  );
}
