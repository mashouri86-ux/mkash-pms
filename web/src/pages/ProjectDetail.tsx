import { useState } from "react";
import { api, type Me, type ProjectView } from "../api";
import { useI18n } from "../i18n";
import { Bar, Chip, Loading, useLoad } from "../ui";
import { LEADS_EN } from "./Projects";

interface ActivityDto {
  id: number;
  title: string;
  plannedStart?: string | null;
  plannedEnd?: string | null;
  progress: number;
  status: "notStarted" | "inProgress" | "done";
  state: "notStarted" | "inProgress" | "overdue" | "done";
  planned?: number | null;
  managerNote?: string | null;
  pendingApproval: boolean;
}

interface Detail {
  project: ProjectView;
  description?: string | null;
  initiative?: { code: string; nameAr: string; nameEn: string } | null;
  canEdit: boolean;
  workPackages: { id: number; name: string; phase: string; activities: ActivityDto[] }[];
  risks: { id: number; titleAr: string; titleEn?: string | null; score: number; owner?: string | null; status: string }[];
}

interface ClosureDto {
  id: number;
  status: string;
  summary: string;
  lessons?: string | null;
  returnNote?: string | null;
  deliverables: { id: number; name: string; progress: number; acceptance: string; acceptanceNote?: string | null }[];
  canDecide: boolean;
  canApprove: boolean;
}

const STATE_COLOR: Record<string, string> = { done: "var(--good)", overdue: "var(--critical)", inProgress: "var(--navy)", notStarted: "var(--neutral)" };

export default function ProjectDetail({ id, me }: { id: number; me: Me }) {
  const i = useI18n();
  const { data, error, reload } = useLoad(() => api.get<Detail>(`/projects/${id}`), [id]);
  const closure = useLoad(() => api.get<ClosureDto | undefined>(`/projects/${id}/closure`), [id]);
  const [edits, setEdits] = useState<Record<number, { progress: number; status: ActivityDto["status"] }>>({});
  const [msg, setMsg] = useState<string | null>(null);
  const [summary, setSummary] = useState("");

  if (!data) return <Loading error={error} retry={reload} />;
  const p = data.project;

  const save = async (a: ActivityDto) => {
    const e = edits[a.id];
    if (!e) return;
    setMsg(null);
    try {
      await api.put(`/projects/${id}/activities/${a.id}`, { progress: e.progress, status: e.status, note: null, actualStart: null, actualEnd: null });
      setEdits((cur) => {
        const next = { ...cur };
        delete next[a.id];
        return next;
      });
      reload();
    } catch (err) {
      setMsg((err as Error).message);
    }
  };

  const act = async (fn: () => Promise<unknown>) => {
    setMsg(null);
    try {
      await fn();
      closure.reload();
      reload();
    } catch (err) {
      setMsg((err as Error).message);
    }
  };

  const c = closure.data;
  return (
    <>
      <div className="row no-print" style={{ marginBottom: 12 }}>
        <button className="btn" onClick={() => (window.location.hash = "projects")}>
          {i.lang === "ar" ? "→" : "←"} {i.t("back")}
        </button>
      </div>
      <section className="hero">
        <div className="row">
          <span className="pcode" style={{ background: "rgba(255,255,255,.15)", color: "#fff" }}>
            {p.code}
          </span>
          <Chip status={p.status} />
        </div>
        <h1 style={{ marginTop: 8 }}>{i.pick(p.nameAr, p.nameEn)}</h1>
        <p>
          {data.initiative && `${data.initiative.code} ${i.pick(data.initiative.nameAr, data.initiative.nameEn)} · `}
          {p.sectorLead && i.pick(p.sectorLead, LEADS_EN[p.sectorLead])} · {p.managerName}
        </p>
        <div className="tiles">
          <div className="tile">
            <b className="num">{i.num(p.progress)}%</b>
            <span>{i.t("progress")}</span>
          </div>
          <div className="tile">
            <b className="num">{i.num(p.planned)}%</b>
            <span>{i.t("planned")}</span>
          </div>
          <div className="tile">
            <b className="num">{i.money(p.budget)}</b>
            <span>{i.t("budget")}</span>
          </div>
          <div className="tile">
            <b className="num">{p.activities}</b>
            <span>{i.t("activities")}</span>
          </div>
          {me.showsOverdue && (
            <div className="tile">
              <b className="num">{p.overdue ?? 0}</b>
              <span>{i.t("overdue")}</span>
            </div>
          )}
        </div>
      </section>

      {msg && <div className="err">{msg}</div>}

      <div className="card mt">
        <h2>{i.t("workPackages")}</h2>
        {data.workPackages.map((w) => (
          <div key={w.id} style={{ marginBottom: 18 }}>
            <div className="row">
              <b>{w.name}</b>
              <span className="muted">· {w.phase}</span>
            </div>
            <div className="table-wrap">
              <table>
                <tbody>
                  {w.activities.map((a) => {
                    const e = edits[a.id];
                    return (
                      <tr key={a.id}>
                        <td style={{ width: 6, padding: 0, background: STATE_COLOR[a.state] }} />
                        <td>
                          {a.title}
                          {a.pendingApproval && <div className="muted">⏳ {i.t("pendingApproval")}</div>}
                        </td>
                        <td className="muted num" style={{ whiteSpace: "nowrap" }}>
                          {i.date(a.plannedStart)} – {i.date(a.plannedEnd)}
                        </td>
                        <td style={{ minWidth: 170 }}>
                          {data.canEdit && !p.closed ? (
                            <div className="row" style={{ flexWrap: "nowrap" }}>
                              <input
                                className="field num"
                                type="number"
                                min={0}
                                max={100}
                                style={{ width: 70 }}
                                value={e?.progress ?? a.progress}
                                onChange={(ev) => setEdits({ ...edits, [a.id]: { progress: Number(ev.target.value), status: e?.status ?? a.status } })}
                              />
                              <select
                                className="field"
                                value={e?.status ?? a.status}
                                onChange={(ev) => setEdits({ ...edits, [a.id]: { progress: e?.progress ?? a.progress, status: ev.target.value as ActivityDto["status"] } })}
                              >
                                <option value="notStarted">—</option>
                                <option value="inProgress">▶</option>
                                <option value="done">✓</option>
                              </select>
                              {e && (
                                <button className="btn primary" onClick={() => void save(a)}>
                                  {i.t("save")}
                                </button>
                              )}
                            </div>
                          ) : (
                            <div className="row" style={{ flexWrap: "nowrap" }}>
                              <Bar value={a.progress} marker={a.planned ?? undefined} />
                              <span className="num">{a.progress}%</span>
                            </div>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        ))}
      </div>

      {data.risks.length > 0 && (
        <div className="card mt">
          <h2>{i.t("topRisks")}</h2>
          {data.risks.map((r) => (
            <div key={r.id} className="row" style={{ borderBottom: "1px solid var(--line)", padding: "6px 0" }}>
              <b className="num">{r.score}</b> {i.pick(r.titleAr, r.titleEn)} <span className="muted">{r.owner}</span>
            </div>
          ))}
        </div>
      )}

      <div className="card mt">
        <h2>{i.t("closure")}</h2>
        {c && (
          <>
            <div className="row">
              <Chip status={c.status} />
              <span>{c.summary}</span>
            </div>
            {c.returnNote && <div className="banner mt">↩ {c.returnNote}</div>}
            <table className="mt">
              <tbody>
                {c.deliverables.map((d) => (
                  <tr key={d.id}>
                    <td>{d.name}</td>
                    <td className="num">{d.progress}%</td>
                    <td>
                      <Chip status={d.acceptance === "accepted" ? "approved" : d.acceptance === "rejected" ? "rejected" : "pending"} />
                    </td>
                    <td>
                      {c.canDecide && (
                        <div className="row">
                          <button className="btn" onClick={() => void act(() => api.put(`/projects/${id}/closure/deliverables/${d.id}`, { acceptance: "accepted", note: null }))}>
                            {i.t("accept")}
                          </button>
                          <button
                            className="btn"
                            onClick={() => {
                              const note = window.prompt(i.t("note"));
                              if (note) void act(() => api.put(`/projects/${id}/closure/deliverables/${d.id}`, { acceptance: "rejected", note }));
                            }}
                          >
                            {i.t("reject")}
                          </button>
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {c.canDecide && (
              <div className="row mt">
                <button className="btn primary" disabled={!c.canApprove} onClick={() => void act(() => api.post(`/projects/${id}/closure/approve`, { note: null }))}>
                  {i.t("approveClosure")}
                </button>
                <button
                  className="btn"
                  onClick={() => {
                    const note = window.prompt(i.t("note"));
                    if (note) void act(() => api.post(`/projects/${id}/closure/return`, { note }));
                  }}
                >
                  {i.t("return")}
                </button>
              </div>
            )}
          </>
        )}
        {data.canEdit && !p.closed && (!c || c.status === "returned") && (
          <div className="mt">
            <textarea className="field" placeholder={i.t("summary")} value={summary} onChange={(e) => setSummary(e.target.value)} />
            <button
              className="btn primary mt"
              disabled={!summary.trim()}
              onClick={() => void act(() => api.post(`/projects/${id}/closure`, { summary, lessons: null, deliverableNotes: null }))}
            >
              {i.t("requestClosure")}
            </button>
          </div>
        )}
      </div>
    </>
  );
}
