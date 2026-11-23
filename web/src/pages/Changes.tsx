import { useEffect, useState } from "react";
import { api, type Me, type ProjectView } from "../api";
import { useI18n } from "../i18n";
import { Chip, Loading, useLoad } from "../ui";

type Row = [string, string, string?];
interface Lists {
  types: Record<string, Row[]>;
  entities: Row[];
  bodies: Row[];
}

type Form = Record<string, unknown> & { impact?: Record<string, { on: string; d: string }> };

interface CrListItem {
  id: number;
  code?: string | null;
  projectId: number;
  projectCode: string;
  projectNameAr: string;
  status: string;
  form: Form;
  createdAt: string;
  submittedAt?: string | null;
  canDecide: boolean;
}

interface CrDetail extends CrListItem {
  apply?: Record<string, string> | null;
  npc?: { approval?: Record<string, string> } | null;
  returnNote?: string | null;
  history: { action: string; note?: string | null; at: string }[];
  canEdit: boolean;
  canRecordNpc: boolean;
}

const LEVELS: [string, string][] = [
  ["مبادرة", "Initiative"],
  ["مشروع", "Project"],
  ["مؤشر أداء", "KPI"],
];
const YN: [string, string][] = [
  ["نعم", "Yes"],
  ["لا", "No"],
];
const IMPACT: [string, string, string][] = [
  ["budget", "الميزانية", "Budget"],
  ["timeline", "الجدول الزمني", "Timeline"],
  ["scope", "النطاق", "Scope"],
  ["outputs", "المخرجات", "Outputs"],
  ["other", "أخرى", "Other"],
];
/** Project fields applied automatically when the NPC approves, by change type (same as the API). */
const APPLY: Record<string, string[]> = {
  "تغيير اسم المشروع": ["nameAr", "name"],
  "تغيير وصف المشروع": ["description"],
  "تغيير ممثل المشروع": ["manager"],
  "تغيير ربط المشروع بمبادرات أخرى": ["initiative"],
  "تغيير الميزانية المخطط لها للمشروع": ["budget"],
  "تغيير الجدول الزمني للمشروع (بما في ذلك تاريخ البدء والانتهاء)": ["start", "end"],
  "إضافة مشروع جديد": ["code", "nameAr", "name", "manager", "initiative", "start", "end", "budget"],
};

function blankForm(p: ProjectView, requester: string): Form {
  return {
    date: new Date().toISOString().slice(0, 10),
    requester,
    initCode: p.initiativeCode ? `SD.${p.initiativeCode}` : "",
    initName: "",
    ownerEntity: "",
    level: "مشروع",
    type: "",
    projCode: /^\d/.test(p.code) ? `SD.${p.code}` : p.code,
    projName: p.nameAr,
    projOwner: "",
    linked: "لا",
    current: "",
    proposed: "",
    reasons: "",
    affected: "",
    impact: Object.fromEntries(IMPACT.map(([k]) => [k, { on: "لا", d: "" }])),
  };
}

export default function Changes({ me, id }: { me: Me; id?: number }) {
  const i = useI18n();
  const list = useLoad(() => api.get<CrListItem[]>("/change-requests"), []);
  const lists = useLoad(() => api.get<Lists>("/change-requests/lists"), []);
  const projects = useLoad(() => api.get<ProjectView[]>("/projects"), []);
  const [newFor, setNewFor] = useState<number | "">("");
  const [err, setErr] = useState<string | null>(null);

  if (id) return <ChangeRequest id={id} lists={lists.data} />;
  if (!list.data) return <Loading error={list.error} retry={list.reload} />;

  const editable = (projects.data ?? []).filter((p) => me.role === "pmo" || me.managedProjects.includes(p.id));
  const create = async () => {
    const p = editable.find((x) => x.id === newFor);
    if (!p) return;
    setErr(null);
    try {
      const r = await api.post<{ id: number }>("/change-requests", { projectId: p.id, form: blankForm(p, i.pick(me.nameAr, me.nameEn)), apply: null, npc: null });
      window.location.hash = `changes/${r.id}`;
    } catch (e) {
      setErr((e as Error).message);
    }
  };

  return (
    <div className="card">
      <div className="row" style={{ marginBottom: 12 }}>
        <h2 style={{ margin: 0 }}>{i.t("changes")}</h2>
        <span className="spacer" />
        {editable.length > 0 && (
          <>
            <select className="field" value={newFor} onChange={(e) => setNewFor(e.target.value ? Number(e.target.value) : "")}>
              <option value="">{i.t("projects")}…</option>
              {editable.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.code} {i.pick(p.nameAr, p.nameEn)}
                </option>
              ))}
            </select>
            <button className="btn primary" disabled={!newFor} onClick={() => void create()}>
              ＋
            </button>
          </>
        )}
      </div>
      {err && <div className="err">{err}</div>}
      <table>
        <thead>
          <tr>
            <th>{i.t("code")}</th>
            <th>{i.t("name")}</th>
            <th>Type</th>
            <th>{i.t("status")}</th>
          </tr>
        </thead>
        <tbody>
          {list.data.map((c) => (
            <tr key={c.id} className="click" onClick={() => (window.location.hash = `changes/${c.id}`)}>
              <td className="num" dir="ltr">
                {c.code ?? "—"}
              </td>
              <td>
                <span className="pcode">{c.projectCode}</span> {c.projectNameAr}
              </td>
              <td>{String(c.form.type ?? "—")}</td>
              <td>
                <Chip status={c.status} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ChangeRequest({ id, lists }: { id: number; lists?: Lists }) {
  const i = useI18n();
  const { data, error, reload } = useLoad(() => api.get<CrDetail>(`/change-requests/${id}`), [id]);
  const [f, setF] = useState<Form>({});
  const [ap, setAp] = useState<Record<string, string>>({});
  const [npc, setNpc] = useState<Record<string, string>>({});
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    if (!data) return;
    setF(data.form);
    setAp(data.apply ?? {});
    setNpc(data.npc?.approval ?? {});
  }, [data]);

  if (!data || !lists) return <Loading error={error} retry={reload} />;
  const ed = data.canEdit;
  const pair = (rows: [string, string, ...unknown[]][], v: unknown) => {
    const r = rows.find((x) => x[0] === v);
    return r ? i.pick(r[0], r[1]) : String(v ?? "");
  };
  const str = (k: string) => String(f[k] ?? "");
  const set = (k: string, v: unknown) => setF({ ...f, [k]: v });
  const types = lists.types[str("level")] ?? [];
  const applyKeys = str("level") === "مشروع" ? APPLY[str("type")] : undefined;

  const field = (k: string, label: string, kind: "text" | "date" | "area" | "select" = "text", rows?: [string, string, ...unknown[]][]) => (
    <label key={k} style={{ display: "grid", gap: 4, gridColumn: kind === "area" ? "1 / -1" : undefined }}>
      <span className="muted">{label}</span>
      {!ed ? (
        <div>{kind === "select" && rows ? pair(rows, f[k]) || "—" : str(k) || "—"}</div>
      ) : kind === "select" && rows ? (
        <select className="field" value={str(k)} onChange={(e) => set(k, e.target.value)}>
          <option value="">—</option>
          {rows.map((r) => (
            <option key={r[0]} value={r[0]}>
              {i.pick(r[0], r[1])}
            </option>
          ))}
        </select>
      ) : kind === "area" ? (
        <textarea className="field" value={str(k)} onChange={(e) => set(k, e.target.value)} />
      ) : (
        <input className="field" type={kind} value={str(k)} onChange={(e) => set(k, e.target.value)} />
      )}
    </label>
  );

  const run = async (fn: () => Promise<unknown>) => {
    setMsg(null);
    try {
      await fn();
      reload();
    } catch (e) {
      setMsg((e as Error).message);
    }
  };
  const save = () => api.put(`/change-requests/${id}`, { projectId: data.projectId, form: f, apply: ap, npc: data.canRecordNpc ? { approval: npc } : null });
  const note = () => window.prompt(i.t("noteRequired"));

  return (
    <>
      <div className="card">
        <div className="row">
          <button className="btn" onClick={() => (window.location.hash = "changes")}>
            {i.lang === "ar" ? "→" : "←"} {i.t("back")}
          </button>
          <b className="num" dir="ltr" style={{ fontSize: 18 }}>
            {data.code ?? "—"}
          </b>
          <Chip status={data.status} />
          <span className="muted">
            {data.projectCode} · {data.projectNameAr}
          </span>
          <span className="spacer" />
          {ed && (
            <>
              <button className="btn" onClick={() => void run(save)}>
                {i.t("save")}
              </button>
              <button className="btn primary" onClick={() => void run(async () => { await save(); await api.post(`/change-requests/${id}/submit`); })}>
                {i.t("submit")}
              </button>
            </>
          )}
          {data.canDecide && (
            <>
              <button className="btn primary" onClick={() => void run(() => api.post(`/change-requests/${id}/approve`, { note: null }))}>
                {i.t("approve")}
              </button>
              <button className="btn" onClick={() => { const n = note(); if (n) void run(() => api.post(`/change-requests/${id}/return`, { note: n })); }}>
                {i.t("return")}
              </button>
            </>
          )}
          <button className="btn" onClick={() => void api.download(`/change-requests/${id}/excel`, `${data.code ?? "change-request-draft"}.xlsm`)}>
            {i.t("excel")}
          </button>
        </div>
        {data.returnNote && <div className="banner mt">↩ {data.returnNote}</div>}
        {msg && <div className="err">{msg}</div>}
      </div>

      <div className="card mt">
        <h2>1. {i.pick("معلومات طلب التغيير", "Change request information")}</h2>
        <div className="grid g3">
          {field("date", "1.2 " + i.pick("تاريخ تقديم الطلب", "Submission date"), "date")}
          {field("requester", "1.3 " + i.pick("الاسم الثلاثي لمقدم الطلب", "Requester full name"))}
          {field("initCode", "1.4 " + i.pick("رمز المبادرة", "Initiative code"))}
          {field("initName", "1.5 " + i.pick("اسم المبادرة", "Initiative name"))}
          {field("ownerEntity", "1.6 " + i.pick("الجهة المالكة للمبادرة", "Initiative owner entity"), "select", lists.entities as [string, string][])}
          {field("level", "1.7 " + i.pick("مستوى التغيير", "Change level"), "select", LEVELS)}
          {field("type", "1.8 " + i.pick("فئة نوع التغيير", "Change type"), "select", types as [string, string][])}
          {str("level") === "مشروع" && field("projCode", "1.7.1 " + i.pick("رمز المشروع", "Project code"))}
          {str("level") === "مشروع" && field("projName", "1.7.2 " + i.pick("اسم المشروع", "Project name"))}
          {str("level") === "مشروع" && field("projOwner", "1.7.3 " + i.pick("الجهة المالكة للمشروع", "Project owner entity"), "select", lists.entities as [string, string][])}
          {str("level") === "مؤشر أداء" && field("kpiCode", "1.7.4 " + i.pick("رمز مؤشر الأداء", "KPI code"))}
          {str("level") === "مؤشر أداء" && field("kpiName", "1.7.5 " + i.pick("اسم مؤشر الأداء", "KPI name"))}
          {str("level") === "مؤشر أداء" && field("kpiOwner", "1.7.6 " + i.pick("الجهة المالكة للمؤشر", "KPI owner entity"), "select", lists.entities as [string, string][])}
          {field("linked", "1.9 " + i.pick("مرتبط بطلبات تغيير أخرى؟", "Linked to other change requests?"), "select", YN)}
          {str("linked") === "نعم" && field("linkedCodes", "1.10 " + i.pick("رموز طلبات التغيير الأخرى", "Other change request codes"))}
        </div>
      </div>

      <div className="card mt">
        <h2>2. {i.pick("تفاصيل التغيير المطلوب", "Details of the requested change")}</h2>
        <div className="grid g2">
          {field("affected", "2.5 " + i.pick("الجهات المتأثرة", "Entities affected"))}
          {field("current", "2.2 " + i.pick("القيمة الحالية", "Current value"), "area")}
          {field("proposed", "2.3 " + i.pick("القيمة المقترحة", "Proposed value"), "area")}
          {field("reasons", "2.4 " + i.pick("أسباب التغيير والفوائد والأثر", "Reasons, benefits and impact if not implemented"), "area")}
        </div>
        {applyKeys && applyKeys.length > 0 && (
          <div className="banner mt">
            <b>{i.pick("القيم التي ستُطبَّق على المشروع عند موافقة المجلس", "Values applied to the project when the NPC approves")}</b>
            <div className="grid g3 mt">
              {applyKeys.map((k) => (
                <label key={k} style={{ display: "grid", gap: 4 }}>
                  <span className="muted">{k}</span>
                  {ed ? (
                    <input className="field" type={k === "start" || k === "end" ? "date" : k === "budget" ? "number" : "text"} value={ap[k] ?? ""} onChange={(e) => setAp({ ...ap, [k]: e.target.value })} />
                  ) : (
                    <div>{ap[k] || "—"}</div>
                  )}
                </label>
              ))}
            </div>
          </div>
        )}
      </div>

      <div className="card mt">
        <h2>3. {i.pick("أثر التغيير المطلوب", "Impact of the requested change")}</h2>
        <table>
          <tbody>
            {IMPACT.map(([k, a, e]) => {
              const x = f.impact?.[k] ?? { on: "لا", d: "" };
              const setX = (v: { on: string; d: string }) => set("impact", { ...(f.impact ?? {}), [k]: v });
              return (
                <tr key={k}>
                  <td>{i.pick(a, e)}</td>
                  <td>
                    {ed ? (
                      <select className="field" value={x.on} onChange={(ev) => setX({ ...x, on: ev.target.value })}>
                        {YN.map((r) => (
                          <option key={r[0]} value={r[0]}>
                            {i.pick(r[0], r[1])}
                          </option>
                        ))}
                      </select>
                    ) : (
                      pair(YN, x.on)
                    )}
                  </td>
                  <td style={{ width: "60%" }}>
                    {ed && x.on === "نعم" ? <textarea className="field" value={x.d} onChange={(ev) => setX({ ...x, d: ev.target.value })} /> : x.d || "—"}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {(data.canRecordNpc || ["withNpc", "approved", "rejected"].includes(data.status)) && (
        <div className="card mt">
          <h2>5. {i.pick("الموافقات", "Approvals")} (NPC)</h2>
          <div className="grid g3">
            <label style={{ display: "grid", gap: 4 }}>
              <span className="muted">5.1 {i.pick("الجهة المسؤولة عن الموافقة", "Approving body")}</span>
              {data.canRecordNpc ? (
                <select className="field" value={npc.body ?? ""} onChange={(e) => setNpc({ ...npc, body: e.target.value })}>
                  <option value="">—</option>
                  {lists.bodies.map((b) => (
                    <option key={b[0]} value={b[0]}>
                      {i.pick(b[0], b[1])}
                    </option>
                  ))}
                </select>
              ) : (
                <div>{npc.body || "—"}</div>
              )}
            </label>
            <label style={{ display: "grid", gap: 4 }}>
              <span className="muted">5.3 {i.pick("التاريخ", "Date")}</span>
              {data.canRecordNpc ? <input className="field" type="date" value={npc.date ?? ""} onChange={(e) => setNpc({ ...npc, date: e.target.value })} /> : <div>{npc.date || "—"}</div>}
            </label>
            <label style={{ display: "grid", gap: 4 }}>
              <span className="muted">5.4 {i.pick("الملاحظات", "Notes")}</span>
              {data.canRecordNpc ? <input className="field" value={npc.notes ?? ""} onChange={(e) => setNpc({ ...npc, notes: e.target.value })} /> : <div>{npc.notes || "—"}</div>}
            </label>
          </div>
          {data.canRecordNpc && (
            <div className="row mt">
              {(
                [
                  ["approved", "موافق عليه", "npcYes"],
                  ["rejected", "مرفوض", "npcNo"],
                  ["moreInfo", "يتطلب المزيد من المعلومات", "npcMore"],
                ] as const
              ).map(([d, label, key]) => (
                <button
                  key={d}
                  className={`btn ${d === "approved" ? "primary" : ""}`}
                  onClick={() =>
                    void run(() =>
                      api.post(`/change-requests/${id}/npc`, {
                        decision: d,
                        npc: { approval: { ...npc, decision: label, date: npc.date || new Date().toISOString().slice(0, 10), body: npc.body || "المجلس الوطني للتخطيط" } },
                      }),
                    )
                  }
                >
                  {i.t(key)}
                </button>
              ))}
            </div>
          )}
        </div>
      )}

      <div className="card mt">
        <h2>{i.t("history")}</h2>
        {data.history.map((h, k) => (
          <div key={k} className="muted">
            {i.date(h.at)} · {h.action}
            {h.note && ` · “${h.note}”`}
          </div>
        ))}
      </div>
    </>
  );
}
