import { useCallback, useEffect, useState, type ReactNode } from "react";
import { useI18n } from "./i18n";

/** Load data from the API with loading and error states. */
export function useLoad<T>(load: () => Promise<T>, deps: unknown[]): { data: T | undefined; error: string | null; reload: () => void } {
  const [data, setData] = useState<T>();
  const [error, setError] = useState<string | null>(null);
  const [n, setN] = useState(0);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const run = useCallback(load, deps);
  useEffect(() => {
    let live = true;
    setError(null);
    run()
      .then((d) => live && setData(d))
      .catch((e: Error) => live && setError(e.message));
    return () => {
      live = false;
    };
  }, [run, n]);
  return { data, error, reload: () => setN((x) => x + 1) };
}

export function Loading({ error, retry }: { error?: string | null; retry?: () => void }) {
  const { t } = useI18n();
  if (error)
    return (
      <div className="card">
        <b>{t("error")}</b>
        <div className="err">{error}</div>
        {retry && (
          <button className="btn mt" onClick={retry}>
            {t("retry")}
          </button>
        )}
      </div>
    );
  return <div className="muted">{t("loading")}</div>;
}

export function Chip({ status }: { status: string }) {
  const { t } = useI18n();
  return <span className={`chip s-${status}`}>{t(status)}</span>;
}

export function Bar({ value, marker }: { value: number; marker?: number }) {
  return (
    <div className="bar" title={`${Math.round(value)}%`}>
      <i style={{ width: `${Math.max(0, Math.min(100, value))}%` }} />
      {marker !== undefined && <u style={{ insetInlineStart: `${Math.max(0, Math.min(100, marker))}%` }} />}
    </div>
  );
}

export const STATUS_COLORS: Record<string, string> = {
  onTrack: "#2f7d4f",
  atRisk: "#b7791f",
  delayed: "#b42318",
  onHold: "#8a8a84",
  completed: "#0d4261",
  closed: "#5b6b7a",
  cancelled: "#c9c6bd",
};

/** Donut with a centre label. */
export function Donut({ parts, label, size = 150 }: { parts: { value: number; color: string }[]; label: ReactNode; size?: number }) {
  const total = parts.reduce((s, p) => s + p.value, 0) || 1;
  const r = size / 2 - 12;
  const c = 2 * Math.PI * r;
  let offset = 0;
  return (
    <div style={{ position: "relative", width: size, height: size }}>
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} style={{ transform: "rotate(-90deg)" }}>
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="#eeebe3" strokeWidth={16} />
        {parts.map((p, i) => {
          const len = (p.value / total) * c;
          const el = (
            <circle key={i} cx={size / 2} cy={size / 2} r={r} fill="none" stroke={p.color} strokeWidth={16} strokeDasharray={`${len} ${c - len}`} strokeDashoffset={-offset} />
          );
          offset += len;
          return el;
        })}
      </svg>
      <div style={{ position: "absolute", inset: 0, display: "grid", placeItems: "center", textAlign: "center" }}>{label}</div>
    </div>
  );
}

/** Horizontal bars, values already in the unit to show. */
export function HBars({ rows, format }: { rows: { label: string; value: number; color?: string; sub?: string }[]; format: (n: number) => string }) {
  const max = Math.max(1, ...rows.map((r) => r.value));
  return (
    <div className="grid" style={{ gap: 8 }}>
      {rows.map((r, i) => (
        <div key={i} style={{ display: "grid", gridTemplateColumns: "minmax(120px, 34%) 1fr auto", gap: 10, alignItems: "center", fontSize: 13 }}>
          <span title={r.label} style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
            {r.label}
          </span>
          <div className="bar" style={{ height: 10 }}>
            <i style={{ width: `${(r.value / max) * 100}%`, background: r.color ?? "var(--navy)" }} />
          </div>
          <span className="num" style={{ minWidth: 70, textAlign: "end" }}>
            {format(r.value)}
            {r.sub && <span className="muted"> · {r.sub}</span>}
          </span>
        </div>
      ))}
    </div>
  );
}
