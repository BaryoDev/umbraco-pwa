// PWA installs dashboard.
//
// Deliberately a plain custom element with no imports and no build step. The backoffice already
// ships the uui-* components used here, and a package that needs npm and a bundler to render one
// table is a package that rots the first time the toolchain moves. The tradeoff is no Lit
// templating, which for a single view is not much of a loss.

const API = "/umbraco/management/api/v1/baryodev/pwa";

class BaryoDevPwaDashboard extends HTMLElement {
  #summary = null;
  #readiness = null;
  #rows = [];
  #state = "loading"; // loading | ready | error
  #error = "";
  #installedOnly = false;

  connectedCallback() {
    this.#render();
    this.#load();
  }

  async #load() {
    this.#state = "loading";
    this.#render();

    try {
      const [summary, readiness, rows] = await Promise.all([
        this.#get(`${API}/summary`),
        this.#get(`${API}/readiness`),
        this.#get(`${API}/installs?installedOnly=${this.#installedOnly}`),
      ]);
      this.#summary = summary;
      this.#readiness = readiness;
      this.#rows = rows;
      this.#state = "ready";
    } catch (err) {
      this.#error = err && err.message ? err.message : "Could not load install data.";
      this.#state = "error";
    }

    this.#render();
  }

  async #get(url) {
    // The backoffice attaches its own auth to same-origin fetches through this token, so the
    // dashboard never handles credentials itself.
    const token = window.Umbraco?.Sys?.ServerVariables?.umbracoUrls?.authToken;
    const res = await fetch(url, {
      credentials: "same-origin",
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });

    if (res.status === 401 || res.status === 403) {
      throw new Error("You do not have access to the Settings section.");
    }
    if (!res.ok) {
      throw new Error(`Request failed (${res.status}).`);
    }
    return res.json();
  }

  #onToggleInstalled = (e) => {
    this.#installedOnly = !!e.target.checked;
    this.#load();
  };

  #render() {
    this.innerHTML = `
      <style>
        .pwa-grid { display: grid; gap: var(--uui-size-space-4, 12px); }
        .pwa-stats {
          display: grid; gap: var(--uui-size-space-4, 12px);
          grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
        }
        .pwa-stat {
          background: var(--uui-color-surface, #fff);
          border: 1px solid var(--uui-color-border, #d8d7d9);
          border-radius: var(--uui-border-radius, 3px);
          padding: var(--uui-size-space-5, 16px);
        }
        .pwa-stat h3 {
          margin: 0 0 4px; font-size: 12px; font-weight: 700;
          letter-spacing: .06em; text-transform: uppercase;
          color: var(--uui-color-text-alt, #68676b);
        }
        .pwa-stat .v { font-size: 30px; font-weight: 300; line-height: 1.1;
                       font-variant-numeric: tabular-nums; }
        .pwa-stat .sub { font-size: 12px; color: var(--uui-color-text-alt, #68676b); }
        .pwa-ready {
          border-radius: var(--uui-border-radius, 3px);
          padding: var(--uui-size-space-4, 12px) var(--uui-size-space-5, 16px);
          border-left: 3px solid;
          font-size: 14px;
        }
        .pwa-ready.ok   { border-color: var(--uui-color-positive, #2b8e57);
                          background: var(--uui-color-positive-standalone, #eaf5ee); }
        .pwa-ready.warn { border-color: var(--uui-color-warning, #fab00f);
                          background: var(--uui-color-warning-standalone, #fdf5e3); }
        .pwa-ready.bad  { border-color: var(--uui-color-danger, #d42054);
                          background: var(--uui-color-danger-standalone, #fbeaef); }
        .pwa-ready ul { margin: 8px 0 0; padding-left: 18px; display: grid; gap: 8px; }
        .pwa-detail { color: var(--uui-color-text-alt, #68676b); font-size: 13px; }
        .pwa-advisory {
          font-size: 10px; text-transform: uppercase; letter-spacing: .06em;
          color: var(--uui-color-text-alt, #68676b);
        }
        .pwa-scroll { overflow-x: auto; }
        table { width: 100%; border-collapse: collapse; font-size: 14px; }
        th {
          text-align: left; padding: 8px 12px 8px 0; white-space: nowrap;
          font-size: 11px; letter-spacing: .06em; text-transform: uppercase;
          color: var(--uui-color-text-alt, #68676b);
          border-bottom: 1px solid var(--uui-color-border, #d8d7d9);
        }
        td {
          padding: 10px 12px 10px 0;
          border-bottom: 1px solid var(--uui-color-divider, #f3f3f5);
          font-variant-numeric: tabular-nums;
        }
        td.mono { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 12px; }
        .pwa-toolbar { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
        .pwa-empty { padding: 32px 0; color: var(--uui-color-text-alt, #68676b); max-width: 60ch; }
      </style>

      <uui-box headline="Progressive web app">
        <div class="pwa-grid">${this.#body()}</div>
      </uui-box>
    `;

    const toggle = this.querySelector("#installed-only");
    if (toggle) toggle.addEventListener("change", this.#onToggleInstalled);

    const retry = this.querySelector("#retry");
    if (retry) retry.addEventListener("click", () => this.#load());
  }

  #body() {
    if (this.#state === "loading") {
      return `<uui-loader-bar></uui-loader-bar>`;
    }

    if (this.#state === "error") {
      return `
        <div class="pwa-empty">
          <p>${escapeHtml(this.#error)}</p>
          <uui-button id="retry" look="secondary" label="Try again"></uui-button>
        </div>`;
    }

    const s = this.#summary || {};
    const rate = s.totalDevices ? Math.round((s.installed / s.totalDevices) * 100) : 0;
    const platforms = Object.entries(s.byPlatform || {});

    return `
      <div class="pwa-stats">
        ${stat("Installed", s.installed ?? 0, "browsers that added the app")}
        ${stat("Install rate", `${rate}%`, `of ${(s.totalDevices ?? 0).toLocaleString()} seen`)}
        ${stat("Active", s.activeLast30Days ?? 0, "installed, last 30 days")}
        ${stat(
          "Top platform",
          platforms.length ? platforms[0][0] : "none yet",
          platforms.length ? `${platforms[0][1]} of ${s.installed}` : "no installs recorded",
        )}
      </div>

      ${this.#readinessPanel()}

      <div class="pwa-toolbar">
        <uui-toggle id="installed-only" label="Installed only" ${
          this.#installedOnly ? "checked" : ""
        }></uui-toggle>
      </div>

      ${this.#table()}
    `;
  }

  // Browsers enforce installability silently. A site can look completely fine and simply never
  // offer to install, which is exactly what happened on this package's own demo when the
  // manifest icons 404'd. Showing the failing condition turns a mystery into a to-do.
  #readinessPanel() {
    const r = this.#readiness;
    if (!r) return "";

    const failing = (r.checks || []).filter((c) => !c.passed);
    if (!failing.length) {
      return `<div class="pwa-ready ok">
        <strong>This site is installable.</strong>
        Visitors on Android and desktop Chrome will be offered the install prompt.
      </div>`;
    }

    const blocking = failing.filter((c) => !c.advisory);
    const rows = failing
      .map(
        (c) => `<li><strong>${escapeHtml(c.name)}</strong>${
          c.advisory ? ' <span class="pwa-advisory">advisory</span>' : ""
        }<br><span class="pwa-detail">${escapeHtml(c.detail)}</span></li>`,
      )
      .join("");

    return `<div class="pwa-ready ${blocking.length ? "bad" : "warn"}">
      <strong>${
        blocking.length
          ? "This site is not installable yet."
          : "Installable, with one recommendation."
      }</strong>
      <ul>${rows}</ul>
    </div>`;
  }

  #table() {
    if (!this.#rows.length) {
      return `
        <div class="pwa-empty">
          <p><strong>Nothing recorded yet.</strong></p>
          <p>Add <code>&lt;script src="/baryodev-pwa.js" defer&gt;&lt;/script&gt;</code> and
          <code>&lt;link rel="manifest" href="/manifest.webmanifest"&gt;</code> to your site
          layout, then load a page on the front end. Reports arrive on the next launch.</p>
        </div>`;
    }

    const rows = this.#rows
      .map(
        (r) => `
        <tr>
          <td class="mono">${escapeHtml(shortId(r.deviceId))}</td>
          <td>${escapeHtml(r.platform || "other")}</td>
          <td>${
            r.installed
              ? `<uui-tag color="positive" look="secondary">installed</uui-tag>`
              : `<uui-tag look="secondary">browser</uui-tag>`
          }</td>
          <td>${escapeHtml(r.displayMode)}</td>
          <td>${Number(r.launchCount || 0).toLocaleString()}</td>
          <td>${formatDate(r.installedAt)}</td>
          <td>${formatDate(r.lastSeenAt)}</td>
        </tr>`,
      )
      .join("");

    return `
      <div class="pwa-scroll">
        <table>
          <thead>
            <tr>
              <th>Device</th><th>Platform</th><th>State</th><th>Last mode</th>
              <th>Launches</th><th>Installed</th><th>Last seen</th>
            </tr>
          </thead>
          <tbody>${rows}</tbody>
        </table>
      </div>`;
  }
}

function stat(label, value, sub) {
  return `
    <div class="pwa-stat">
      <h3>${escapeHtml(label)}</h3>
      <div class="v">${escapeHtml(String(value))}</div>
      <div class="sub">${escapeHtml(sub)}</div>
    </div>`;
}

// The device id is generated by the visitor's browser, so it is not sensitive, but showing it in
// full invites treating it as an identity. A prefix is enough to tell two rows apart.
function shortId(id) {
  return id && id.length > 12 ? `${id.slice(0, 12)}...` : id || "";
}

function formatDate(value) {
  if (!value) return "&mdash;";
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? "&mdash;" : d.toLocaleDateString();
}

// Every value interpolated into the markup above goes through this, without exception.
// It matters more here than it looks: `deviceId` and `platform` arrive from an anonymous public
// endpoint, are stored, and are then rendered inside a signed-in administrator's browser. That is
// a stored-XSS path if anything reaches innerHTML raw. The server truncates and whitelists on the
// way in; this is the second half of the same job.
function escapeHtml(value) {
  return String(value ?? "").replace(
    /[&<>"']/g,
    (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c],
  );
}

customElements.define("baryodev-pwa-dashboard", BaryoDevPwaDashboard);
export default BaryoDevPwaDashboard;
