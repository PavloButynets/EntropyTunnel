import { useState } from 'react'
import type { RoutingRule } from '../types'
import * as api from '../api/client'

interface Props {
  rules: RoutingRule[]
  onRefresh: () => void
}

type ModalMode = 'none' | 'create' | 'edit'
type FormState = Omit<RoutingRule, 'id'>

const DEFAULT_FORM: FormState = {
  name: '',
  pathPattern: '/api/*',
  targetBaseUrl: 'http://localhost:5000',
  isEnabled: true,
  priority: 0,
}

// ── Validation ────────────────────────────────────────────────────────────────
function validate(form: FormState): Record<string, string> {
  const e: Record<string, string> = {}
  if (!form.name.trim()) e.name = 'Name is required'
  if (!form.pathPattern.trim()) e.pathPattern = 'Path pattern is required'
  if (!form.targetBaseUrl.trim()) {
    e.targetBaseUrl = 'Target URL is required'
  } else if (!/^https?:\/\/.+/.test(form.targetBaseUrl)) {
    e.targetBaseUrl = 'Must start with http:// or https://'
  }
  if (form.priority < 0) e.priority = 'Priority must be ≥ 0'
  return e
}

// ── Component ─────────────────────────────────────────────────────────────────
export function RoutingRules({ rules, onRefresh }: Props) {
  const [mode, setMode] = useState<ModalMode>('none')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<FormState>(DEFAULT_FORM)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [apiError, setApiError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // ── Actions ─────────────────────────────────────────────────────────────────

  function openCreate() {
    setForm(DEFAULT_FORM)
    setErrors({})
    setApiError(null)
    setEditingId(null)
    setMode('create')
  }

  function openEdit(rule: RoutingRule) {
    const { id: _, ...rest } = rule
    setForm(rest)
    setErrors({})
    setApiError(null)
    setEditingId(rule.id)
    setMode('edit')
  }

  function closeModal() {
    setMode('none')
    setEditingId(null)
    setErrors({})
    setApiError(null)
  }

  function set<K extends keyof FormState>(field: K, value: FormState[K]) {
    setForm(f => ({ ...f, [field]: value }))
    if (errors[field]) setErrors(e => { const n = { ...e }; delete n[field]; return n })
  }

  async function handleSave() {
    const errs = validate(form)
    if (Object.keys(errs).length > 0) { setErrors(errs); return }

    setSaving(true)
    setApiError(null)
    try {
      if (mode === 'edit' && editingId) {
        await api.updateRoutingRule(editingId, { ...form, id: editingId })
      } else {
        await api.addRoutingRule(form)
      }
      closeModal()
      onRefresh()
    } catch (err) {
      setApiError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete(rule: RoutingRule) {
    if (!confirm(`Delete routing rule "${rule.name}"?`)) return
    await api.deleteRoutingRule(rule.id)
    onRefresh()
  }

  // ── Render ──────────────────────────────────────────────────────────────────
  return (
    <section>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <p style={{ color: 'var(--text-muted)', fontSize: 13 }}>
          Route path prefixes to different local services (e.g. /api/* → :5000, rest → :5173).
        </p>
        <button className="primary" onClick={openCreate}>+ Add Route</button>
      </div>

      {rules.length === 0
        ? <div className="empty-state">No routing rules — all traffic goes to the default local port.</div>
        : (
          <table>
            <thead>
              <tr>
                <th>Priority</th>
                <th>Name</th>
                <th>Pattern</th>
                <th>Target</th>
                <th>Enabled</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {rules.map(rule => (
                <tr key={rule.id} style={{ opacity: rule.isEnabled ? 1 : 0.45 }}>
                  <td><span className="badge badge-muted">{rule.priority}</span></td>
                  <td><strong>{rule.name}</strong></td>
                  <td><code>{rule.pathPattern}</code></td>
                  <td><code style={{ color: 'var(--accent-h)' }}>{rule.targetBaseUrl}</code></td>
                  <td>
                    <span className={`badge ${rule.isEnabled ? 'badge-green' : 'badge-muted'}`}>
                      {rule.isEnabled ? 'ON' : 'OFF'}
                    </span>
                  </td>
                  <td>
                    <div className="actions">
                      <button onClick={() => openEdit(rule)}>Edit</button>
                      <button className="danger" onClick={() => handleDelete(rule)}>Delete</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

      {/* ── Modal ─────────────────────────────────────────────────────────── */}
      {mode !== 'none' && (
        <div className="modal-backdrop" onClick={e => e.target === e.currentTarget && closeModal()}>
          <div className="modal">
            <div className="modal-header">
              <h3>{mode === 'edit' ? 'Edit Routing Rule' : 'New Routing Rule'}</h3>
              <button className="icon" onClick={closeModal}>✕</button>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {apiError && (
                <div className="api-error">
                  <span className="api-error-icon">✕</span>
                  <span>{apiError}</span>
                </div>
              )}

              <div className="form-row">
                <div className="form-group">
                  <label>Rule Name *</label>
                  <input
                    value={form.name}
                    onChange={e => set('name', e.target.value)}
                    placeholder="API backend"
                    className={errors.name ? 'input-error' : ''}
                  />
                  {errors.name && <span className="field-error">{errors.name}</span>}
                </div>
                <div className="form-group">
                  <label>Priority (lower = matched first)</label>
                  <input
                    type="number" min={0}
                    value={form.priority}
                    onChange={e => set('priority', +e.target.value)}
                    className={errors.priority ? 'input-error' : ''}
                  />
                  {errors.priority && <span className="field-error">{errors.priority}</span>}
                </div>
              </div>

              <div className="form-group">
                <label>Path Pattern *</label>
                <input
                  value={form.pathPattern}
                  onChange={e => set('pathPattern', e.target.value)}
                  placeholder="/api/*"
                  className={errors.pathPattern ? 'input-error' : ''}
                />
                {errors.pathPattern && <span className="field-error">{errors.pathPattern}</span>}
              </div>

              <div className="form-group">
                <label>Target Base URL *</label>
                <input
                  value={form.targetBaseUrl}
                  onChange={e => set('targetBaseUrl', e.target.value)}
                  placeholder="http://localhost:5000"
                  className={errors.targetBaseUrl ? 'input-error' : ''}
                />
                {errors.targetBaseUrl && <span className="field-error">{errors.targetBaseUrl}</span>}
              </div>

              <div className="form-group">
                <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                  <button
                    className={`toggle ${form.isEnabled ? 'on' : ''}`}
                    onClick={() => set('isEnabled', !form.isEnabled)}
                  />
                  {form.isEnabled ? 'Enabled' : 'Disabled'}
                </label>
              </div>
            </div>

            <div className="modal-footer">
              <button onClick={closeModal}>Cancel</button>
              <button className="primary" onClick={handleSave} disabled={saving}>
                {saving ? 'Saving…' : mode === 'edit' ? 'Save Changes' : 'Add Route'}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  )
}
