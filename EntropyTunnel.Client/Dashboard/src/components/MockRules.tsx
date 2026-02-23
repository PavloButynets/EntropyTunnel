import { useState } from 'react'
import type { MockRule } from '../types'
import * as api from '../api/client'

interface Props {
  rules: MockRule[]
  onRefresh: () => void
}

type ModalMode = 'none' | 'create' | 'edit'
type FormState = Omit<MockRule, 'id'>

const DEFAULT_FORM: FormState = {
  name: '',
  pathPattern: '/api/users',
  method: 'GET',
  isEnabled: true,
  statusCode: 200,
  contentType: 'application/json',
  responseBody: '{\n  "data": []\n}',
}

// ── Validation ────────────────────────────────────────────────────────────────
function validate(form: FormState): Record<string, string> {
  const e: Record<string, string> = {}
  if (!form.name.trim()) e.name = 'Name is required'
  if (!form.pathPattern.trim()) e.pathPattern = 'Path pattern is required'
  if (form.statusCode < 100 || form.statusCode > 599)
    e.statusCode = 'Must be a valid HTTP status (100–599)'
  if (!form.contentType.trim()) e.contentType = 'Content-Type is required'
  return e
}

function statusBadge(code: number) {
  const cls = code < 300 ? 'badge-green' : code < 400 ? 'badge-yellow' : 'badge-red'
  return <span className={`badge ${cls}`}>{code}</span>
}

// ── Component ─────────────────────────────────────────────────────────────────
export function MockRules({ rules, onRefresh }: Props) {
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

  function openEdit(rule: MockRule) {
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
        await api.updateMockRule(editingId, { ...form, id: editingId })
      } else {
        await api.addMockRule(form)
      }
      closeModal()
      onRefresh()
    } catch (err) {
      setApiError(err instanceof Error ? err.message : 'Unknown error')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete(rule: MockRule) {
    if (!confirm(`Delete mock "${rule.name}"?`)) return
    await api.deleteMockRule(rule.id)
    onRefresh()
  }

  // ── Render ──────────────────────────────────────────────────────────────────
  return (
    <section>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <p style={{ color: 'var(--text-muted)', fontSize: 13 }}>
          Return canned responses — useful when the backend isn't ready.
        </p>
        <button className="primary" onClick={openCreate}>+ Add Mock</button>
      </div>

      {rules.length === 0
        ? <div className="empty-state">No mock responses yet.</div>
        : (
          <table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Pattern</th>
                <th>Method</th>
                <th>Status</th>
                <th>Content-Type</th>
                <th>Enabled</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {rules.map(rule => (
                <tr key={rule.id} style={{ opacity: rule.isEnabled ? 1 : 0.45 }}>
                  <td><strong>{rule.name}</strong></td>
                  <td><code>{rule.pathPattern}</code></td>
                  <td><span className="badge badge-muted">{rule.method ?? 'ANY'}</span></td>
                  <td>{statusBadge(rule.statusCode)}</td>
                  <td style={{ color: 'var(--text-muted)', fontSize: 12 }}>{rule.contentType}</td>
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
              <h3>{mode === 'edit' ? 'Edit Mock Response' : 'New Mock Response'}</h3>
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
                    placeholder="Empty users list"
                    className={errors.name ? 'input-error' : ''}
                  />
                  {errors.name && <span className="field-error">{errors.name}</span>}
                </div>
                <div className="form-group">
                  <label>HTTP Method</label>
                  <select value={form.method ?? ''} onChange={e => set('method', e.target.value || null)}>
                    <option value="">Any</option>
                    {['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'OPTIONS'].map(m =>
                      <option key={m} value={m}>{m}</option>)}
                  </select>
                </div>
              </div>

              <div className="form-group">
                <label>Path Pattern *</label>
                <input
                  value={form.pathPattern}
                  onChange={e => set('pathPattern', e.target.value)}
                  placeholder="/api/users"
                  className={errors.pathPattern ? 'input-error' : ''}
                />
                {errors.pathPattern && <span className="field-error">{errors.pathPattern}</span>}
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>Status Code *</label>
                  <input
                    type="number"
                    value={form.statusCode}
                    onChange={e => set('statusCode', +e.target.value)}
                    className={errors.statusCode ? 'input-error' : ''}
                  />
                  {errors.statusCode && <span className="field-error">{errors.statusCode}</span>}
                </div>
                <div className="form-group">
                  <label>Content-Type *</label>
                  <select
                    value={form.contentType}
                    onChange={e => set('contentType', e.target.value)}
                    className={errors.contentType ? 'input-error' : ''}
                  >
                    <option value="application/json">application/json</option>
                    <option value="text/plain">text/plain</option>
                    <option value="text/html">text/html</option>
                    <option value="application/xml">application/xml</option>
                    <option value="application/octet-stream">application/octet-stream</option>
                  </select>
                  {errors.contentType && <span className="field-error">{errors.contentType}</span>}
                </div>
              </div>

              <div className="form-group">
                <label>Response Body</label>
                <textarea
                  value={form.responseBody}
                  onChange={e => set('responseBody', e.target.value)}
                  rows={6}
                />
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
                {saving ? 'Saving…' : mode === 'edit' ? 'Save Changes' : 'Add Mock'}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  )
}
