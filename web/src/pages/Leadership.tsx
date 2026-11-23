import { useState } from "react";
import { api, type ProjectView } from "../api";
import { useI18n } from "../i18n";
import { Bar, Chip, Donut, HBars, Loading, STATUS_COLORS, useLoad } from "../ui";
import { LEADS_EN, LeadFilter } from "./Projects";

interface Summary {
  projects: number;
  budget: number;
  spent: number;
  progress: number;
  planned: number;
  byStatus: Record<string, number>;
  byOutcome: { outcome: number; projects: number; budget: number; progress: number; planned: number }[];
  byInitiative: { initiative: string; projects: number; budget: number; spent: number; progress: number }[];
  byLead: { lead: string; projects: number; progress: number; planned: number; delayed: number }[];
  needsAttention: ProjectView[];
  topRisks: { id: number; projectId: number; titleAr: string; titleEn?: string | null; score: number; owner?: string | null }[];
  decisions: { changeRequests: number; closures: number };
}

const OUTCOME_COLORS = ["#3F8452", "#0D4261", "#8A1538", "#B7791F", "#5B6B7A", "#2B7A8C"];

/** Executive view: approved figures only, no overdue activities, filter by sector lead. */
export default function Leadership() {
  const i = useI18n();
  const [lead, setLead] = useState("");
  const { data, error, reload } = useLoad(() => api.get<Summary>(`/leadership${lead ? `?lead=${encodeURIComponent(lead)}` : ""}`), [lead]);
  if (!data) return <Loading error={error} retry={reload} />;
  const d = data;
  const status = Object.entries(d.byStatus);

  return (
    <>
      <section className="hero">
        <div className="row">
          <div>
            <h1>{i.t("leadership")}</h1>
            <p>{lead ? i.pick(lead, LEADS_EN[lead]) : i.t("allLeads")}</p>
          </div>
          <span className="spacer" />
          <div className="no-print">
            <LeadFilter value={lead} onChange={setLead} />
          </div>
        </div>
        <div className="tiles">
          <div className="tile">
            <b className="num">{d.projects}</b>
            <span>{i.t("projects")}</span>
          </div>
          <div className="tile">
            <b className="num">{i.num(d.progress)}%</b>
            <span>
              {i.t("progress")} · {i.t("planned")} {i.num(d.planned)}%
            </span>
          </div>
          <div className="tile">
            <b className="num">{i.money(d.budget)}</b>
            <span>
              {i.t("budget")} · {i.t("spent")} {i.money(d.spent)}
            </span>
          </div>
          <div className="tile">
            <b className="num">{(d.byStatus.delayed ?? 0) + (d.byStatus.atRisk ?? 0)}</b>
            <span>{i.t("needsAttention")}</span>
          </div>
          <div className="tile">
            <b className="num">{d.decisions.changeRequests + d.decisions.closures}</b>
            <span>{i.t("decisions")}</span>
          </div>
        </div>
      </section>

      <div className="grid g2 mt">
        <div className="card">
          <h2>{i.t("status")}</h2>
          <div className="row" style={{ gap: 24 }}>
            <Donut
              parts={status.map(([s, n]) => ({ value: n, color: STATUS_COLORS[s] ?? "#999" }))}
              label={
                <div>
                  <b style={{ fontSize: 26 }}>{d.projects}</b>
                  <div className="muted">{i.t("projects")}</div>
                </div>
              }
            />
            <div className="grid" style={{ gap: 6 }}>
              {status.map(([s, n]) => (
                <div key={s} className="row">
                  <Chip status={s} /> <b className="num">{n}</b>
                </div>
              ))}
            </div>
          </div>
        </div>
        <div className="card">
          <h2>{i.t("byOutcome")}</h2>
          <div className="grid" style={{ gap: 10 }}>
            {d.byOutcome.map((o) => (
              <div key={o.outcome} style={{ display: "grid", gridTemplateColumns: "28px 1fr 60px", gap: 10, alignItems: "center" }}>
                <b style={{ color: OUTCOME_COLORS[(o.outcome - 1 + 6) % 6] }}>{o.outcome}</b>
                <Bar value={o.progress} marker={o.planned} />
                <span className="num">{i.num(o.progress)}%</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      <div className="grid g2 mt">
        <div className="card">
          <h2>{i.t("budgetByInitiative")}</h2>
          <HBars
            rows={d.byInitiative.map((x) => ({ label: x.initiative, value: x.budget, sub: `${i.num(x.progress)}%`, color: OUTCOME_COLORS[(Number(x.initiative.split(".")[0]) - 1 + 6) % 6] }))}
            format={i.money}
          />
        </div>
        <div className="card">
          <h2>{i.t("byLead")}</h2>
          <table>
            <tbody>
              {d.byLead.map((l) => (
                <tr key={l.lead} className="click" onClick={() => setLead(l.lead === "—" ? "" : l.lead)}>
                  <td>{i.pick(l.lead, LEADS_EN[l.lead])}</td>
                  <td className="num">{l.projects}</td>
                  <td style={{ minWidth: 140 }}>
                    <Bar value={l.progress} marker={l.planned} />
                  </td>
                  <td className="num">{i.num(l.progress)}%</td>
                  <td className="num" style={{ color: l.delayed ? "var(--critical)" : undefined }}>
                    {l.delayed} {i.t("delayed")}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="grid g2 mt">
        <div className="card">
          <h2>{i.t("needsAttention")}</h2>
          <table>
            <tbody>
              {d.needsAttention.map((p) => (
                <tr key={p.id} className="click" onClick={() => (window.location.hash = `projects/${p.id}`)}>
                  <td>
                    <span className="pcode">{p.code}</span>
                  </td>
                  <td>{i.pick(p.nameAr, p.nameEn)}</td>
                  <td className="num">{i.num(p.variance, 1)}</td>
                  <td>
                    <Chip status={p.status} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="card">
          <h2>{i.t("topRisks")}</h2>
          {d.topRisks.length === 0 && <div className="muted">—</div>}
          {d.topRisks.map((r) => (
            <div key={r.id} className="row" style={{ padding: "6px 0", borderBottom: "1px solid var(--line)" }}>
              <b className="num" style={{ color: r.score >= 15 ? "var(--critical)" : "var(--warning)" }}>
                {r.score}
              </b>
              {i.pick(r.titleAr, r.titleEn)}
              <span className="muted">{r.owner}</span>
            </div>
          ))}
          <div className="muted mt">
            {d.decisions.changeRequests} {i.t("crWaiting")} · {d.decisions.closures} {i.t("closuresWaiting")}
          </div>
        </div>
      </div>
    </>
  );
}
