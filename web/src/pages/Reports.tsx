import { useState } from "react";
import { api, type ProjectView } from "../api";
import { useI18n } from "../i18n";
import { Bar, Chip, Loading, useLoad } from "../ui";

interface Report {
  period: { frequency: string; start: string; end: string; key: string };
  projects: ProjectView[];
  activity: { updates: number; approved: number; pending: number; returned: number; projectsUpdated: number; changeRequests: number; projectsClosed: number };
}

const FREQS = ["biweekly", "monthly", "quarterly", "yearly"] as const;

/** Status report for any bi-weekly, monthly, quarterly or yearly period. Print to save as PDF. */
export default function Reports() {
  const i = useI18n();
  const [freq, setFreq] = useState<(typeof FREQS)[number]>("biweekly");
  const [back, setBack] = useState(0);
  const { data, error, reload } = useLoad(() => api.get<Report>(`/reports?frequency=${freq}&back=${back}`), [freq, back]);

  return (
    <>
      <div className="card no-print">
        <div className="row">
          {FREQS.map((f) => (
            <button key={f} className={`btn ${freq === f ? "primary" : ""}`} onClick={() => { setFreq(f); setBack(0); }}>
              {i.t(f)}
            </button>
          ))}
          <span className="spacer" />
          <button className="btn" onClick={() => setBack(back + 1)}>
            {i.t("previous")}
          </button>
          <button className="btn" disabled={back === 0} onClick={() => setBack(Math.max(0, back - 1))}>
            {i.t("next")}
          </button>
          <button className="btn" onClick={() => window.print()}>
            {i.t("print")}
          </button>
        </div>
      </div>
      {!data ? (
        <Loading error={error} retry={reload} />
      ) : (
        <>
          <section className="hero mt">
            <div className="muted" style={{ color: "rgba(255,255,255,.75)" }}>
              {i.t("brand")} · {i.t(data.period.frequency)}
            </div>
            <h1>
              {i.date(data.period.start)} – {i.date(data.period.end)}
            </h1>
            <div className="tiles">
              <div className="tile">
                <b className="num">{data.projects.length}</b>
                <span>{i.t("projects")}</span>
              </div>
              <div className="tile">
                <b className="num">{data.activity.projectsUpdated}</b>
                <span>{i.t("projectsUpdated")}</span>
              </div>
              <div className="tile">
                <b className="num">{data.activity.updates}</b>
                <span>
                  {i.t("updates")} · {data.activity.pending} {i.t("pending")}
                </span>
              </div>
              <div className="tile">
                <b className="num">{data.activity.changeRequests}</b>
                <span>{i.t("changes")}</span>
              </div>
              <div className="tile">
                <b className="num">{data.activity.projectsClosed}</b>
                <span>{i.t("closed")}</span>
              </div>
            </div>
          </section>
          <div className="card mt">
            <table>
              <thead>
                <tr>
                  <th>{i.t("code")}</th>
                  <th>{i.t("name")}</th>
                  <th>{i.t("progress")}</th>
                  <th>{i.t("planned")}</th>
                  <th>{i.t("status")}</th>
                </tr>
              </thead>
              <tbody>
                {data.projects.map((p) => (
                  <tr key={p.id}>
                    <td>
                      <span className="pcode">{p.code}</span>
                    </td>
                    <td>{i.pick(p.nameAr, p.nameEn)}</td>
                    <td style={{ minWidth: 150 }}>
                      <div className="row" style={{ flexWrap: "nowrap" }}>
                        <Bar value={p.progress} marker={p.planned} />
                        <span className="num">{i.num(p.progress)}%</span>
                      </div>
                    </td>
                    <td className="num">{i.num(p.planned)}%</td>
                    <td>
                      <Chip status={p.status} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </>
  );
}
