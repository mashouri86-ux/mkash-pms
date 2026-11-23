import { useState } from "react";
import { api } from "../api";
import { useI18n } from "../i18n";
import { Bar, Chip, Loading, useLoad } from "../ui";

interface KpiDto {
  id: string;
  category: "strategic" | "ministry";
  nameAr: string;
  nameEn?: string | null;
  outcome: number;
  unit?: string | null;
  baseline?: number | null;
  baselineYear?: string | null;
  target?: number | null;
  targetYear?: string | null;
  current?: number | null;
  achievement?: number | null;
  delivery: { linkedProjects: number; progress: number; planned: number; variance: number; status?: string | null };
  projects: { id: number; code: string; status: string; impact: number }[];
}

/** KPIs: PMO and executives only. Delivery = impact-weighted progress of linked projects. */
export default function Kpis() {
  const i = useI18n();
  const [cat, setCat] = useState<"strategic" | "ministry">("strategic");
  const { data, error, reload } = useLoad(() => api.get<KpiDto[]>(`/kpis?category=${cat}`), [cat]);
  const v = (n?: number | null) => (n === null || n === undefined ? "—" : i.num(n, 1));

  return (
    <div className="card">
      <div className="row" style={{ marginBottom: 12 }}>
        {(["strategic", "ministry"] as const).map((c) => (
          <button key={c} className={`btn ${cat === c ? "primary" : ""}`} onClick={() => setCat(c)}>
            {i.t(c)}
          </button>
        ))}
      </div>
      {!data ? (
        <Loading error={error} retry={reload} />
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{i.t("code")}</th>
                <th>KPI</th>
                <th>{i.t("baseline")}</th>
                <th>{i.t("current")}</th>
                <th>{i.t("target")}</th>
                <th>{i.t("delivery")}</th>
                <th>{i.t("status")}</th>
              </tr>
            </thead>
            <tbody>
              {data.map((k) => (
                <tr key={k.id}>
                  <td>
                    <span className="pcode">{k.id}</span>
                  </td>
                  <td>
                    {i.pick(k.nameAr, k.nameEn)}
                    <div className="muted">
                      {k.delivery.linkedProjects} {i.t("linkedProjects")}
                      {k.projects.length > 0 && ` · ${k.projects.map((p) => p.code).join(", ")}`}
                    </div>
                  </td>
                  <td className="num">
                    {v(k.baseline)} <span className="muted">{k.baselineYear}</span>
                  </td>
                  <td className="num">{v(k.current)}</td>
                  <td className="num">
                    {v(k.target)} <span className="muted">{k.targetYear}</span>
                  </td>
                  <td style={{ minWidth: 150 }}>
                    {k.delivery.linkedProjects > 0 ? (
                      <div className="row" style={{ flexWrap: "nowrap" }}>
                        <Bar value={k.delivery.progress} marker={k.delivery.planned} />
                        <span className="num">{i.num(k.delivery.progress)}%</span>
                      </div>
                    ) : (
                      "—"
                    )}
                  </td>
                  <td>{k.delivery.status ? <Chip status={k.delivery.status} /> : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
