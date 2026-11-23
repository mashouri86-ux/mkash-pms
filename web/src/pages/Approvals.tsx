import { useState } from "react";
import { api } from "../api";
import { useI18n } from "../i18n";
import { Chip, Loading, useLoad } from "../ui";

interface UpdateDto {
  id: number;
  projectId: number;
  code: string;
  projectNameAr: string;
  kind: string;
  title?: string | null;
  changesJson?: string | null;
  note?: string | null;
  submittedBy?: string | null;
  submittedAt: string;
  status: string;
  reviewNote?: string | null;
  canDecide: boolean;
}

interface Change {
  ActivityId: number;
  Before: { Progress: number; Status: string };
  After: { Progress: number; Status: string };
}

/** Progress updates waiting for the sector lead or the PMO; managers see the status of theirs. */
export default function Approvals() {
  const i = useI18n();
  const [status, setStatus] = useState("pending");
  const [err, setErr] = useState<string | null>(null);
  const { data, error, reload } = useLoad(() => api.get<UpdateDto[]>(`/approvals?status=${status}`), [status]);

  const decide = async (u: UpdateDto, approve: boolean) => {
    setErr(null);
    let note: string | null = null;
    if (!approve) {
      note = window.prompt(i.t("noteRequired"));
      if (!note) return;
    }
    try {
      await api.post(`/approvals/${u.id}/${approve ? "approve" : "return"}`, { note });
      reload();
    } catch (e) {
      setErr((e as Error).message);
    }
  };

  const changes = (json?: string | null): Change[] => {
    try {
      return json ? (JSON.parse(json) as Change[]) : [];
    } catch {
      return [];
    }
  };

  return (
    <div className="card">
      <div className="row" style={{ marginBottom: 12 }}>
        <h2 style={{ margin: 0 }}>{i.t("approvals")}</h2>
        <span className="spacer" />
        {["pending", "approved", "returned"].map((s) => (
          <button key={s} className={`btn ${status === s ? "primary" : ""}`} onClick={() => setStatus(s)}>
            {i.t(s)}
          </button>
        ))}
      </div>
      {err && <div className="err">{err}</div>}
      {!data ? (
        <Loading error={error} retry={reload} />
      ) : data.length === 0 ? (
        <div className="muted">{i.t("nothingPending")}</div>
      ) : (
        <table>
          <tbody>
            {data.map((u) => (
              <tr key={u.id}>
                <td>
                  <span className="pcode">{u.code}</span>
                </td>
                <td>
                  <b>{u.title}</b>
                  <div className="muted">
                    {changes(u.changesJson)
                      .map((c) => `${c.Before.Progress}% → ${c.After.Progress}%`)
                      .join(" · ")}
                    {u.note && ` · “${u.note}”`}
                  </div>
                  {u.reviewNote && <div className="muted">↩ {u.reviewNote}</div>}
                </td>
                <td className="muted">
                  {u.submittedBy} · {i.date(u.submittedAt)}
                </td>
                <td>
                  <Chip status={u.status} />
                </td>
                <td>
                  {u.canDecide && (
                    <div className="row">
                      <button className="btn primary" onClick={() => void decide(u, true)}>
                        {i.t("approve")}
                      </button>
                      <button className="btn" onClick={() => void decide(u, false)}>
                        {i.t("return")}
                      </button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
