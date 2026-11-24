/*
 * Tamkeen — system data layer for the dashboard.
 *
 * The dashboard was built against a small set of capabilities (db, user, assets, downloads).
 * This file provides the same capabilities on top of the Tamkeen server:
 *   db        → /api/docs   (SQL Server document store, live updates by polling /api/sync)
 *   user      → /api/me, /api/profiles, /api/users/search   (the signed-in ministry account)
 *   assets    → /api/assets (uploads), /_blob/{id} (downloads)
 *   downloads → browser download
 * Sign-in is a server cookie (Microsoft Entra ID in production, a test form in development),
 * so every request below is already authenticated.
 */
(function () {
  "use strict";

  const POLL_MS = 4000;

  function apiError(status, message) {
    const e = new Error(message || "Request failed (" + status + ")");
    e.code = status === 403 ? "permission_denied" : status === 401 ? "unauthenticated" : status === 413 ? "too_large" : "unavailable";
    e.status = status;
    return e;
  }

  async function call(method, url, body, isForm) {
    const opt = { method, credentials: "same-origin", headers: { Accept: "application/json" } };
    if (body !== undefined) {
      if (isForm) opt.body = body;
      else {
        opt.headers["Content-Type"] = "application/json";
        opt.body = JSON.stringify(body);
      }
    }
    const res = await fetch(url, opt);
    if (res.status === 401) {
      // Session ended: go back through sign-in.
      location.href = "/signin?returnUrl=" + encodeURIComponent(location.pathname + location.hash);
      throw apiError(401);
    }
    if (res.status === 204) return null;
    const text = await res.text();
    let data = null;
    try {
      data = text ? JSON.parse(text) : null;
    } catch {
      data = null; // not the Tamkeen server answering (e.g. the test system has stopped)
    }
    if (!res.ok && (res.status >= 502 || (res.status === 404 && !data))) {
      const e = apiError(res.status, "The Tamkeen server isn't reachable right now (the test system may have stopped). Reopen it and try again — nothing was saved.");
      e.code = "unavailable";
      throw e;
    }
    if (!res.ok) throw apiError(res.status, data && data.error);
    return data;
  }

  // ---------------- db ----------------
  const cache = {}; // collection -> Map(id -> data)
  const listeners = {}; // collection -> [callback]
  let since = 0;
  let polling = false;

  function snapshot(coll) {
    const m = cache[coll] || new Map();
    const docs = [];
    m.forEach((data, id) => docs.push({ id, exists: true, data: () => JSON.parse(JSON.stringify(data)) }));
    return { docs, size: docs.length, empty: docs.length === 0, metadata: { fromCache: false, hasPendingWrites: false } };
  }

  function emit(coll) {
    (listeners[coll] || []).forEach((cb) => {
      try {
        cb(snapshot(coll));
      } catch (e) {
        console.error(e);
      }
    });
  }

  async function poll() {
    try {
      const r = await call("GET", "/api/sync?since=" + since);
      since = r.now;
      const touched = new Set();
      for (const c of r.changes) {
        if (!cache[c.collection]) continue; // nobody listens to it
        if (c.deleted) cache[c.collection].delete(c.id);
        else cache[c.collection].set(c.id, c.data);
        touched.add(c.collection);
      }
      touched.forEach(emit);
    } catch (e) {
      if (e.status !== 401) console.warn("Tamkeen: live update failed, retrying", e);
    }
    setTimeout(poll, POLL_MS);
  }

  function collection(coll) {
    return {
      onSnapshot(cb, onError) {
        (listeners[coll] = listeners[coll] || []).push(cb);
        if (cache[coll]) {
          setTimeout(() => cb(snapshot(coll)), 0);
        } else {
          call("GET", "/api/docs/" + encodeURIComponent(coll))
            .then((r) => {
              cache[coll] = new Map(r.docs.map((d) => [d.id, d.data]));
              if (!since || r.now < since) since = r.now;
              emit(coll);
              if (!polling) {
                polling = true;
                setTimeout(poll, POLL_MS);
              }
            })
            .catch((e) => onError && onError(e));
        }
        return () => {
          listeners[coll] = (listeners[coll] || []).filter((x) => x !== cb);
        };
      },
      doc(id) {
        const url = "/api/docs/" + encodeURIComponent(coll) + "/" + encodeURIComponent(id);
        return {
          id,
          async get() {
            const d = cache[coll] && cache[coll].get(id);
            return { id, exists: !!d, data: () => d };
          },
          async set(data) {
            await call("PUT", url, data);
            (cache[coll] = cache[coll] || new Map()).set(id, JSON.parse(JSON.stringify(data)));
            emit(coll);
          },
          async delete() {
            await call("DELETE", url);
            if (cache[coll]) cache[coll].delete(id);
            emit(coll);
          },
        };
      },
    };
  }

  // ---------------- user ----------------
  let mePromise = null;
  const me = () => (mePromise = mePromise || call("GET", "/api/me"));

  const user = {
    id: async () => (await me()).id,
    me: async () => {
      const m = await me();
      return { id: m.id, email: m.email, name: m.name };
    },
    isOwner: async () => !!(await me()).isOwner,
    canEdit: async () => !!(await me()).canEdit,
    can: async () => true,
    profiles: async (ids) => call("POST", "/api/profiles", { ids }),
    search: async (q) => call("GET", "/api/users/search?q=" + encodeURIComponent(q)),
  };

  // ---------------- assets ----------------
  const assets = {
    async upload(file, opts) {
      const fd = new FormData();
      fd.append("file", file, file.name);
      if (opts && opts.type) fd.append("type", opts.type);
      return call("POST", "/api/assets", fd, true);
    },
    async delete(id) {
      await call("DELETE", "/api/assets/" + encodeURIComponent(id));
    },
  };

  // ---------------- downloads ----------------
  const downloads = {
    async save({ filename, data, mimeType }) {
      const blob = data instanceof Blob ? data : new Blob([data], { type: mimeType || "application/octet-stream" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = filename || "download";
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 5000);
    },
  };

  const caps = { db: { collection }, user, assets, downloads };
  window.claude = {
    use: async (name) => {
      if (!(name in caps)) throw Object.assign(new Error("Capability not available: " + name), { code: "unavailable" });
      return caps[name];
    },
  };

  // When the ministry account is already signed in (Entra ID), start the dashboard session for that
  // account so people don't type a second password. The owner keeps the dashboard's own switch-user screen.
  me()
    .then((m) => {
      try {
        const key = "pmo.session";
        const cur = JSON.parse(localStorage.getItem(key) || "null");
        if (m.autoSession && !m.isOwner && (!cur || cur.email !== m.email)) {
          localStorage.setItem(key, JSON.stringify({ email: m.email, at: new Date().toISOString() }));
          location.reload(); // the dashboard reads its session once at start-up
        }
      } catch (e) {
        /* storage unavailable */
      }
    })
    .catch(() => undefined);
})();
