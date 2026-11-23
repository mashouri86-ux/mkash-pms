import { authHeaders } from "./auth";

export class ApiError extends Error {
  constructor(public status: number, message: string, public body?: unknown) {
    super(message);
  }
}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json", ...(await authHeaders()) };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  const res = await fetch(`/api${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  const data: unknown = text ? JSON.parse(text) : undefined;
  if (!res.ok) {
    const msg = (data as { error?: string } | undefined)?.error ?? res.statusText;
    throw new ApiError(res.status, msg, data);
  }
  return data as T;
}

export const api = {
  get: <T>(p: string) => call<T>("GET", p),
  post: <T>(p: string, b: unknown = {}) => call<T>("POST", p, b),
  put: <T>(p: string, b: unknown) => call<T>("PUT", p, b),
  del: <T>(p: string) => call<T>("DELETE", p),
  async download(p: string, fileName: string): Promise<void> {
    const res = await fetch(`/api${p}`, { headers: await authHeaders() });
    if (!res.ok) throw new ApiError(res.status, res.statusText);
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 2000);
  },
};

// ---- Shapes returned by the API ----

export type Role = "pmo" | "executive" | "lead" | "projectManager";
export type Status = "onTrack" | "atRisk" | "delayed" | "onHold" | "completed" | "closed" | "cancelled";

export interface Me {
  id: number;
  nameAr: string;
  nameEn: string;
  email: string;
  role: Role;
  leadName?: string | null;
  pages: string[];
  managedProjects: number[];
  showsOverdue: boolean;
}

export interface ProjectView {
  id: number;
  code: string;
  nameAr: string;
  nameEn: string;
  sector?: string | null;
  initiativeCode?: string | null;
  portfolio?: number | null;
  sectorLead?: string | null;
  managerName?: string | null;
  budget: number;
  actualSpend: number;
  start?: string | null;
  end?: string | null;
  phase: string;
  status: Status;
  progress: number;
  planned: number;
  variance: number;
  activities: number;
  overdue?: number | null;
  closed: boolean;
}

export interface Bi {
  ar: string;
  en: string;
}

export interface Strategy {
  vision: Bi;
  mission: Bi;
  slogan?: Bi;
  enablers: Bi[];
  outcomes: {
    number: number;
    color: string;
    nameAr: string;
    nameEn: string;
    pillarAr: string;
    pillarEn: string;
    objectives: ({ code: string } & Bi)[];
    initiatives: { code: string; nameAr: string; nameEn: string }[];
  }[];
}
