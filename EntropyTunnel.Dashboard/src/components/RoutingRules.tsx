import { useState } from "react";
import type { RoutingRule } from "../types";
import * as api from "../api/client";
import { useT } from "../i18n";

interface Props {
  rules: RoutingRule[];
  onRefresh: () => void;
}

type ModalMode = "none" | "create" | "edit";
type FormState = Omit<RoutingRule, "id">;

const DEFAULT_FORM: FormState = {
  name: "",
  pathPattern: "/api/*",
  targetBaseUrl: "http://localhost:5000",
  isEnabled: true,
  priority: 0,
};

function validate(
  form: FormState,
  t: ReturnType<typeof useT>["t"],
): Record<string, string> {
  const e: Record<string, string> = {};
  if (!form.name.trim()) e.name = t.routingValidName;
  if (!form.pathPattern.trim()) e.pathPattern = t.routingValidPath;
  if (!form.targetBaseUrl.trim()) {
    e.targetBaseUrl = t.routingValidTarget;
  } else if (!/^https?:\/\/.+/.test(form.targetBaseUrl)) {
    e.targetBaseUrl = t.routingValidTargetFormat;
  }
  if (form.priority < 0) e.priority = t.routingValidPriority;
  return e;
}

export function RoutingRules({ rules, onRefresh }: Props) {
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

  function openEdit(rule: RoutingRule) {
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
        await api.updateRoutingRule(editingId, { ...form, id: editingId });
      } else {
        await api.addRoutingRule(form);
      }
      closeModal();
      onRefresh();
    } catch (err) {
      setApiError(err instanceof Error ? err.message : "Unknown error");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(rule: RoutingRule) {
    if (!confirm(t.routingDeleteConfirm(rule.name))) return;
    await api.deleteRoutingRule(rule.id);
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
          {t.routingDesc}
        </p>
        <button className="primary" onClick={openCreate}>
          {t.routingAdd}
        </button>
      </div>

      {rules.length === 0 ? (
        <div className="empty-state">{t.routingEmpty}</div>
      ) : (
        <table>
          <thead>
            <tr>
              <th>{t.priority}</th>
              <th>{t.name}</th>
              <th>{t.pattern}</th>
              <th>{t.routingTarget}</th>
              <th>{t.enabled}</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rules.map((rule) => (
              <tr key={rule.id} style={{ opacity: rule.isEnabled ? 1 : 0.45 }}>
                <td>
                  <span className="badge badge-muted">{rule.priority}</span>
                </td>
                <td>
                  <strong>{rule.name}</strong>
                </td>
                <td>
                  <code>{rule.pathPattern}</code>
                </td>
                <td>
                  <code style={{ color: "var(--accent-h)" }}>
                    {rule.targetBaseUrl}
                  </code>
                </td>
                <td>
                  <span
                    className={`badge ${rule.isEnabled ? "badge-green" : "badge-muted"}`}
                  >
                    {rule.isEnabled ? "ON" : "OFF"}
                  </span>
                </td>
                <td>
                  <div className="actions">
                    <button onClick={() => openEdit(rule)}>{t.edit}</button>
                    <button
                      className="danger"
                      onClick={() => handleDelete(rule)}
                    >
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
              <h3>
                {mode === "edit" ? t.routingEditTitle : t.routingNewTitle}
              </h3>
              <button className="icon" onClick={closeModal}>
                ✕
              </button>
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
                    placeholder="API backend"
                    className={errors.name ? "input-error" : ""}
                  />
                  {errors.name && (
                    <span className="field-error">{errors.name}</span>
                  )}
                </div>
                <div className="form-group">
                  <label>{t.routingPriorityField}</label>
                  <input
                    type="number"
                    min={0}
                    value={form.priority}
                    onChange={(e) => set("priority", +e.target.value)}
                    className={errors.priority ? "input-error" : ""}
                  />
                  {errors.priority && (
                    <span className="field-error">{errors.priority}</span>
                  )}
                </div>
              </div>

              <div className="form-group">
                <label>{t.chaosPathPattern}</label>
                <input
                  value={form.pathPattern}
                  onChange={(e) => set("pathPattern", e.target.value)}
                  placeholder="/api/*"
                  className={errors.pathPattern ? "input-error" : ""}
                />
                {errors.pathPattern && (
                  <span className="field-error">{errors.pathPattern}</span>
                )}
              </div>

              <div className="form-group">
                <label>{t.routingTargetUrl}</label>
                <input
                  value={form.targetBaseUrl}
                  onChange={(e) => set("targetBaseUrl", e.target.value)}
                  placeholder="http://localhost:5000"
                  className={errors.targetBaseUrl ? "input-error" : ""}
                />
                {errors.targetBaseUrl && (
                  <span className="field-error">{errors.targetBaseUrl}</span>
                )}
              </div>

              <div className="form-group">
                <label
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 8,
                    cursor: "pointer",
                  }}
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
              <button
                className="primary"
                onClick={handleSave}
                disabled={saving}
              >
                {saving
                  ? t.saving
                  : mode === "edit"
                    ? t.saveChanges
                    : t.routingAddBtn}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
