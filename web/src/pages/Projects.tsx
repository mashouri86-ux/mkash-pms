import { useMemo, useState } from "react";
import { api, type Me, type ProjectView } from "../api";
import { useI18n } from "../i18n";
import { Bar, Chip, Donut, Loading, STATUS_COLORS, useLoad } from "../ui";

export const LEADS = ["مها المنصوري", "فهد الخيارين", "مها العطية", "الدكتورة شريفة العمادي", "مكتب الوزير"];
export const LEADS_EN: Record<string, string> = {
  "مها المنصوري": "Maha Al Mansoori",
  "فهد الخيارين": "Fahad Al Khayarin",
  "مها العطية": "Maha Al Attiyah",
  "الدكتورة شريفة العمادي": "Dr. Sharifa Al Emadi",
  "مكتب الوزير": "Minister's Sector",
};

export function LeadFilter({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const i = useI18n();
  return (
    <select className="field" value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">{i.t("allLeads")}</option>
      {LEADS.map((l) => (
        <option key={l} value={l}>
          {i.pick(l, LEADS_EN[l])}
        </option>
      ))}
    </select>
  );
}

export default function Projects({ me }: { me: Me }) {
  const i = useI18n();
  const [lead, setLead] = useState("");
  const [q, setQ] = useState("");
  const { data, error, reload } = useLoad(() => api.get<ProjectView[]>(`/projects${lead ? `?lead=${encodeURIComponent(lead)}` : ""}`), [lead]);
  const rows = useMemo(
    () => (data ?? []).filter((p) => !q || `${p.code} ${p.nameAr} ${p.nameEn} ${p.managerName ?? ""}`.toLowerCase().includes(q.toLowerCase())),
    [data, q],
  );
  if (!data) return <Loading error={error} retry={reload} />;

  const counts = Object.entries(
    rows.reduce<Record<string, number>>((m, p) => ((m[p.status] = (m[p.status] ?? 0) + 1), m), {}),
  );

  return (
    <>
      <div className="grid g2">
        <div className="card">
          <h2>{i.t("status")}</h2>
          <div className="row" style={{ gap: 24 }}>
            <Donut
              parts={counts.map(([s, n]) => ({ value: n, color: STATUS_COLORS[s] ?? "#999" }))}
              label={
                <div>
                  <b style={{ fontSize: 26 }}>{rows.length}</b>
                  <div className="muted">{i.t("projects")}</div>
                </div>
              }
            />
            <div className="grid" style={{ gap: 6 }}>
              {counts.map(([s, n]) => (
                <div key={s} className="row">
                  <Chip status={s} /> <b className="num">{n}</b>
                </div>
              ))}
            </div>
          </div>
        </div>
        <div className="card">
          <h2>{i.t("budget")}</h2>
          <div style={{ fontSize: 28, fontWeight: 700 }} className="num">
            {i.money(rows.reduce((s, p) => s + p.budget, 0))}
          </div>
          <div className="muted">
            {i.t("spent")}: {i.money(rows.reduce((s, p) => s + p.actualSpend, 0))}
          </div>
        </div>
      </div>

      <div className="card mt">
        <div className="row" style={{ marginBottom: 12 }}>
          <h2 style={{ margin: 0 }}>{i.t("projects")}</h2>
          <span className="spacer" />
          <input className="field" placeholder={i.t("search")} value={q} onChange={(e) => setQ(e.target.value)} />
          {me.role !== "projectManager" && me.role !== "lead" && <LeadFilter value={lead} onChange={setLead} />}
        </div>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{i.t("code")}</th>
                <th>{i.t("name")}</th>
                <th>{i.t("sectorLead")}</th>
                <th>{i.t("progress")}</th>
                <th>{i.t("variance")}</th>
                {me.showsOverdue && <th>{i.t("overdue")}</th>}
                <th>{i.t("budget")}</th>
                <th>{i.t("status")}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((p) => (
                <tr key={p.id} className="click" onClick={() => (window.location.hash = `projects/${p.id}`)}>
                  <td>
                    <span className="pcode">{p.code}</span>
                  </td>
                  <td>
                    {i.pick(p.nameAr, p.nameEn)}
                    <div className="muted">{p.managerName}</div>
                  </td>
                  <td className="muted">{p.sectorLead ? i.pick(p.sectorLead, LEADS_EN[p.sectorLead]) : "—"}</td>
                  <td style={{ minWidth: 150 }}>
                    <div className="row" style={{ flexWrap: "nowrap" }}>
                      <Bar value={p.progress} marker={p.planned} />
                      <span className="num">{i.num(p.progress)}%</span>
                    </div>
                  </td>
                  <td className="num" style={{ color: p.variance < -15 ? "var(--critical)" : p.variance < -5 ? "var(--warning)" : undefined }}>
                    {p.variance > 0 ? "+" : ""}
                    {i.num(p.variance, 1)}
                  </td>
                  {me.showsOverdue && <td className="num">{p.overdue ?? 0}</td>}
                  <td className="num">{i.money(p.budget)}</td>
                  <td>
                    <Chip status={p.status} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
}
