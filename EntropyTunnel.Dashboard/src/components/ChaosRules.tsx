import { useState } from "react";
import type { ChaosRule } from "../types";
import * as api from "../api/client";
import { useT } from "../i18n";

interface Props {
  rules: ChaosRule[];
  onRefresh: () => void;
}

type ModalMode = "none" | "create" | "edit";
type FormState = Omit<ChaosRule, "id">;

const DEFAULT_FORM: FormState = {
  name: "",
  pathPattern: "/api/*",
  method: null,
  isEnabled: true,
  latencyMs: 0,
  jitterMs: 0,
  errorRate: 0,
  errorStatusCode: 502,
  errorBody: "Chaos Engineering: Injected Error",
};

function validate(
  form: FormState,
  t: ReturnType<typeof useT>["t"],
): Record<string, string> {
  const e: Record<string, string> = {};
  if (!form.name.trim()) e.name = t.chaosValidName;
  if (!form.pathPattern.trim()) e.pathPattern = t.chaosValidPath;
  if (form.latencyMs < 0) e.latencyMs = t.chaosValidGte0;
  if (form.jitterMs < 0) e.jitterMs = t.chaosValidGte0;
  if (form.errorRate < 0 || form.errorRate > 1) e.errorRate = t.chaosValidErrorRate;
  if (form.errorStatusCode < 100 || form.errorStatusCode > 599)
    e.errorStatusCode = t.chaosValidStatus;
  return e;
}

export function ChaosRules({ rules, onRefresh }: Props) {
  const { t } = useT();
  const [mode, setMode] = useState<ModalMode>("none");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<FormState>(DEFAULT_FORM);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  function openCreate() {
    setForm(DEFAULT_FORM);
    setErrors({});
    setApiError(null);
    setEditingId(null);
    setMode("create");
  }

  function openEdit(rule: ChaosRule) {
    const { id: _, ...rest } = rule;
    setForm(rest);
    setErrors({});
    setApiError(null);
    setEditingId(rule.id);
    setMode("edit");
  }

  function closeModal() {
    setMode("none");
    setEditingId(null);
    setErrors({});
    setApiError(null);
  }

  function set<K extends keyof FormState>(field: K, value: FormState[K]) {
    setForm((f) => ({ ...f, [field]: value }));
    if (errors[field])
      setErrors((e) => {
        const n = { ...e };
        delete n[field];
        return n;
      });
  }

  async function handleSave() {
    const errs = validate(form, t);
    if (Object.keys(errs).length > 0) {
      setErrors(errs);
      return;
    }
    setSaving(true);
    setApiError(null);
    try {
      if (mode === "edit" && editingId) {
        await api.updateChaosRule(editingId, { ...form, id: editingId });
      } else {
        await api.addChaosRule(form);
      }
      closeModal();
      onRefresh();
    } catch (err) {
      setApiError(err instanceof Error ? err.message : "Unknown error");
    } finally {
      setSaving(false);
    }
  }

  async function handleToggle(id: string) {
    try {
      await api.toggleChaosRule(id);
      onRefresh();
    } catch {
      /* ignore */
    }
  }

  async function handleDelete(rule: ChaosRule) {
    if (!confirm(t.chaosDeleteConfirm(rule.name))) return;
    await api.deleteChaosRule(rule.id);
    onRefresh();
  }

  return (
    <section>
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: 16,
        }}
      >
        <p style={{ color: "var(--text-muted)", fontSize: 13 }}>
          {t.chaosDesc}
        </p>
        <button className="primary" onClick={openCreate}>
          {t.chaosAdd}
        </button>
      </div>

      {rules.length === 0 ? (
        <div className="empty-state">{t.chaosEmpty}</div>
      ) : (
        <table>
          <thead>
            <tr>
              <th>{t.name}</th>
              <th>{t.pattern}</th>
              <th>{t.method}</th>
              <th>{t.chaosLatency}</th>
              <th>{t.chaosErrorRate}</th>
              <th>{t.enabled}</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rules.map((rule) => (
              <tr key={rule.id} style={{ opacity: rule.isEnabled ? 1 : 0.45 }}>
                <td>
                  <strong>{rule.name}</strong>
                </td>
                <td>
                  <code>{rule.pathPattern}</code>
                </td>
                <td>
                  <span className="badge badge-muted">
                    {rule.method ?? t.any}
                  </span>
                </td>
                <td>
                  {rule.latencyMs > 0 ? (
                    <span className="badge badge-yellow">
                      {rule.latencyMs}ms
                      {rule.jitterMs > 0 ? ` ±${rule.jitterMs}` : ""}
                    </span>
                  ) : (
                    <span style={{ color: "var(--text-muted)" }}>—</span>
                  )}
                </td>
                <td>
                  {rule.errorRate > 0 ? (
                    <span className="badge badge-red">
                      {(rule.errorRate * 100).toFixed(0)}% →{" "}
                      {rule.errorStatusCode}
                    </span>
                  ) : (
                    <span style={{ color: "var(--text-muted)" }}>—</span>
                  )}
                </td>
                <td>
                  <button
                    className={`toggle ${rule.isEnabled ? "on" : ""}`}
                    onClick={() => handleToggle(rule.id)}
                    title={rule.isEnabled ? t.disabled : t.enabled}
                  />
                </td>
                <td>
                  <div className="actions">
                    <button onClick={() => openEdit(rule)}>{t.edit}</button>
                    <button className="danger" onClick={() => handleDelete(rule)}>
                      {t.delete}
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {mode !== "none" && (
        <div
          className="modal-backdrop"
          onClick={(e) => e.target === e.currentTarget && closeModal()}
        >
          <div className="modal">
            <div className="modal-header">
              <h3>{mode === "edit" ? t.chaosEditTitle : t.chaosNewTitle}</h3>
              <button className="icon" onClick={closeModal}>✕</button>
            </div>

            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              {apiError && (
                <div className="api-error">
                  <span className="api-error-icon">✕</span>
                  <span>{apiError}</span>
                </div>
              )}

              <div className="form-row">
                <div className="form-group">
                  <label>{t.chaosRuleName}</label>
                  <input
                    value={form.name}
                    onChange={(e) => set("name", e.target.value)}
                    placeholder="Slow checkout"
                    className={errors.name ? "input-error" : ""}
                  />
                  {errors.name && <span className="field-error">{errors.name}</span>}
                </div>
                <div className="form-group">
                  <label>{t.chaosHttpMethod}</label>
                  <select
                    value={form.method ?? ""}
                    onChange={(e) => set("method", e.target.value || null)}
                  >
                    <option value="">{t.any}</option>
                    {["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"].map((m) => (
                      <option key={m} value={m}>{m}</option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="form-group">
                <label>{t.chaosPathPattern}</label>
                <input
                  value={form.pathPattern}
                  onChange={(e) => set("pathPattern", e.target.value)}
                  placeholder="/api/checkout  or  /api/*  or  **"
                  className={errors.pathPattern ? "input-error" : ""}
                />
                {errors.pathPattern && <span className="field-error">{errors.pathPattern}</span>}
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>{t.chaosLatencyMs}</label>
                  <input
                    type="number"
                    min={0}
                    value={form.latencyMs}
                    onChange={(e) => set("latencyMs", +e.target.value)}
                    className={errors.latencyMs ? "input-error" : ""}
                  />
                  {errors.latencyMs && <span className="field-error">{errors.latencyMs}</span>}
                </div>
                <div className="form-group">
                  <label>{t.chaosJitterMs}</label>
                  <input
                    type="number"
                    min={0}
                    value={form.jitterMs}
                    onChange={(e) => set("jitterMs", +e.target.value)}
                    className={errors.jitterMs ? "input-error" : ""}
                  />
                  {errors.jitterMs && <span className="field-error">{errors.jitterMs}</span>}
                </div>
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>{t.chaosErrorRateField}</label>
                  <input
                    type="number"
                    min={0}
                    max={100}
                    value={Math.round(form.errorRate * 100)}
                    onChange={(e) =>
                      set("errorRate", Math.min(100, Math.max(0, +e.target.value)) / 100)
                    }
                    className={errors.errorRate ? "input-error" : ""}
                  />
                  {errors.errorRate && <span className="field-error">{errors.errorRate}</span>}
                </div>
                <div className="form-group">
                  <label>{t.chaosErrorCode}</label>
                  <input
                    type="number"
                    value={form.errorStatusCode}
                    onChange={(e) => set("errorStatusCode", +e.target.value)}
                    className={errors.errorStatusCode ? "input-error" : ""}
                  />
                  {errors.errorStatusCode && (
                    <span className="field-error">{errors.errorStatusCode}</span>
                  )}
                </div>
              </div>

              <div className="form-group">
                <label>{t.chaosErrorBody}</label>
                <input
                  value={form.errorBody}
                  onChange={(e) => set("errorBody", e.target.value)}
                  placeholder="Chaos Engineering: Injected Error"
                />
              </div>

              <div className="form-group">
                <label
                  style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}
                >
                  <button
                    className={`toggle ${form.isEnabled ? "on" : ""}`}
                    onClick={() => set("isEnabled", !form.isEnabled)}
                  />
                  {form.isEnabled ? t.enabled : t.disabled}
                </label>
              </div>
            </div>

            <div className="modal-footer">
              <button onClick={closeModal}>{t.cancel}</button>
              <button className="primary" onClick={handleSave} disabled={saving}>
                {saving ? t.saving : mode === "edit" ? t.saveChanges : t.chaosAddBtn}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
