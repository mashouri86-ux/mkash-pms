import { useState } from "react";
import { api, type Role } from "../api";
import { useI18n } from "../i18n";
import { Loading, useLoad } from "../ui";
import { LEADS, LEADS_EN } from "./Projects";

interface Settings {
  updateCycle: string;
  currentCycle: { start: string; end: string; key: string };
  progressMode: string;
}

interface PersonDto {
  id: number;
  nameAr: string;
  nameEn: string;
  email: string;
  role: Role;
  leadName?: string | null;
  active: boolean;
  managedProjectIds: number[];
}

const ROLES: Role[] = ["pmo", "executive", "lead", "projectManager"];

/** PMO administration: update cycle, progress view, people and roles. */
export default function Admin() {
  const i = useI18n();
  const settings = useLoad(() => api.get<Settings>("/admin/settings"), []);
  const people = useLoad(() => api.get<PersonDto[]>("/admin/people"), []);
  const [msg, setMsg] = useState<string | null>(null);
  const [draft, setDraft] = useState({ nameAr: "", nameEn: "", email: "", role: "projectManager" as Role, leadName: "" });

  const run = async (fn: () => Promise<unknown>, after: () => void) => {
    setMsg(null);
    try {
      await fn();
      after();
    } catch (e) {
      setMsg((e as Error).message);
    }
  };

  return (
    <>
      <div className="card">
        <h2>{i.t("updateCycle")}</h2>
        {!settings.data ? (
          <Loading error={settings.error} retry={settings.reload} />
        ) : (
          <div className="row">
            {["biweekly", "monthly", "quarterly"].map((c) => (
              <button
                key={c}
                className={`btn ${settings.data!.updateCycle === c ? "primary" : ""}`}
                onClick={() => void run(() => api.put("/admin/settings", { updateCycle: c, progressMode: null }), settings.reload)}
              >
                {i.t(c)}
              </button>
            ))}
            <span className="muted">
              {i.date(settings.data.currentCycle.start)} – {i.date(settings.data.currentCycle.end)}
            </span>
            <span className="spacer" />
            <span className="muted">{i.t("progressView")}</span>
            {[
              ["percentComplete", "pct"],
              ["weighted", "weighted"],
            ].map(([mode, key]) => (
              <button
                key={mode}
                className={`btn ${settings.data!.progressMode === mode ? "primary" : ""}`}
                onClick={() => void run(() => api.put("/admin/settings", { updateCycle: null, progressMode: mode }), settings.reload)}
              >
                {i.t(key)}
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="card mt">
        <h2>{i.t("people")}</h2>
        {msg && <div className="err">{msg}</div>}
        {!people.data ? (
          <Loading error={people.error} retry={people.reload} />
        ) : (
          <table>
            <thead>
              <tr>
                <th>{i.t("name")}</th>
                <th>{i.t("email")}</th>
                <th>{i.t("role")}</th>
                <th>{i.t("sectorLead")}</th>
                <th>{i.t("projects")}</th>
              </tr>
            </thead>
            <tbody>
              {people.data.map((p) => (
                <tr key={p.id} style={{ opacity: p.active ? 1 : 0.5 }}>
                  <td>{i.pick(p.nameAr, p.nameEn)}</td>
                  <td dir="ltr">{p.email}</td>
                  <td>{i.t(p.role)}</td>
                  <td>{p.leadName ? i.pick(p.leadName, LEADS_EN[p.leadName]) : "—"}</td>
                  <td className="num">{p.managedProjectIds.length || "—"}</td>
                </tr>
              ))}
              <tr>
                <td>
                  <input className="field" placeholder="الاسم" value={draft.nameAr} onChange={(e) => setDraft({ ...draft, nameAr: e.target.value })} />
                  <input className="field" placeholder="Name" value={draft.nameEn} onChange={(e) => setDraft({ ...draft, nameEn: e.target.value })} />
                </td>
                <td>
                  <input className="field" dir="ltr" placeholder="name@ministry" value={draft.email} onChange={(e) => setDraft({ ...draft, email: e.target.value })} />
                </td>
                <td>
                  <select className="field" value={draft.role} onChange={(e) => setDraft({ ...draft, role: e.target.value as Role })}>
                    {ROLES.map((r) => (
                      <option key={r} value={r}>
                        {i.t(r)}
                      </option>
                    ))}
                  </select>
                </td>
                <td>
                  {draft.role === "lead" && (
                    <select className="field" value={draft.leadName} onChange={(e) => setDraft({ ...draft, leadName: e.target.value })}>
                      <option value="">—</option>
                      {LEADS.map((l) => (
                        <option key={l} value={l}>
                          {i.pick(l, LEADS_EN[l])}
                        </option>
                      ))}
                    </select>
                  )}
                </td>
                <td>
                  <button
                    className="btn primary"
                    disabled={!draft.email || !draft.nameAr}
                    onClick={() =>
                      void run(
                        () => api.post("/admin/people", { ...draft, leadName: draft.leadName || null, active: true, managedProjectIds: null }),
                        () => {
                          setDraft({ nameAr: "", nameEn: "", email: "", role: "projectManager", leadName: "" });
                          people.reload();
                        },
                      )
                    }
                  >
                    ＋
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        )}
      </div>
    </>
  );
}
