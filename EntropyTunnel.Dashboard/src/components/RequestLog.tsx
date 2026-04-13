import { useState } from "react";
import type { RequestLogEntry } from "../types";
import type { ReplayPayload, ReplayResponse } from "../api/client";
import * as api from "../api/client";

interface Header {
  key: string;
  value: string;
}

interface ReplayState {
  method: string;
  path: string;
  headers: Header[];
  body: string;
}

function statusClass(code: number) {
  if (code < 300) return "badge-green";
  if (code < 400) return "badge-yellow";
  return "badge-red";
}

function formatTime(iso: string) {
  return new Date(iso).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function tryPrettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

const METHODS_WITH_BODY = new Set(["POST", "PUT", "PATCH"]);

function makeDefaultReplay(entry: RequestLogEntry): ReplayState {
  return {
    method: entry.method,
    path: entry.path,
    headers: entry.requestHeaders
      ? Object.entries(entry.requestHeaders).map(([key, value]) => ({
          key,
          value,
        }))
      : [{ key: "Accept", value: "application/json" }],
    body: entry.requestBodyPreview ?? "",
  };
}

interface HeaderTableProps {
  headers: Record<string, string> | null;
  keyColor: string;
  emptyLabel: string;
}
function HeaderTable({ headers, keyColor, emptyLabel }: HeaderTableProps) {
  const entries = headers ? Object.entries(headers) : [];
  if (entries.length === 0)
    return (
      <span style={{ color: "var(--text-muted)", fontSize: 11 }}>
        {emptyLabel}
      </span>
    );
  return (
    <div style={{ fontFamily: "var(--mono)", fontSize: 11 }}>
      {entries.map(([k, v]) => (
        <div
          key={k}
          style={{
            display: "flex",
            gap: 8,
            marginBottom: 3,
            alignItems: "flex-start",
          }}
        >
          <span
            style={{
              color: keyColor,
              minWidth: 180,
              flexShrink: 0,
              wordBreak: "break-all",
            }}
          >
            {k}
          </span>
          <span style={{ color: "var(--text)", wordBreak: "break-all" }}>
            {v}
          </span>
        </div>
      ))}
    </div>
  );
}

interface Props {
  entries: RequestLogEntry[];
  onRefresh: () => void;
}

export function RequestLog({ entries, onRefresh }: Props) {
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const [replayEntry, setReplayEntry] = useState<RequestLogEntry | null>(null);
  const [replay, setReplay] = useState<ReplayState | null>(null);
  const [response, setResponse] = useState<ReplayResponse | null>(null);
  const [replaying, setReplaying] = useState(false);
  const [replayError, setReplayError] = useState<string | null>(null);
  const [showRespHeaders, setShowRespHeaders] = useState(false);

  async function handleClear() {
    await api.clearRequestLog();
    onRefresh();
  }

  function toggleExpand(id: string) {
    setExpandedId((prev) => (prev === id ? null : id));
  }

  function openReplay(entry: RequestLogEntry, ev: React.MouseEvent) {
    ev.stopPropagation(); // don't toggle expand when clicking Replay
    setReplayEntry(entry);
    setReplay(makeDefaultReplay(entry));
    setResponse(null);
    setReplayError(null);
    setShowRespHeaders(false);
  }

  function closeReplay() {
    setReplayEntry(null);
    setReplay(null);
    setResponse(null);
    setReplayError(null);
  }

  function setField<K extends keyof ReplayState>(
    field: K,
    value: ReplayState[K],
  ) {
    setReplay((r) => (r ? { ...r, [field]: value } : r));
  }

  function addHeader() {
    setReplay((r) =>
      r ? { ...r, headers: [...r.headers, { key: "", value: "" }] } : r,
    );
  }

  function removeHeader(i: number) {
    setReplay((r) =>
      r ? { ...r, headers: r.headers.filter((_, idx) => idx !== i) } : r,
    );
  }

  function updateHeader(i: number, field: "key" | "value", value: string) {
    setReplay((r) => {
      if (!r) return r;
      const headers = r.headers.map((h, idx) =>
        idx === i ? { ...h, [field]: value } : h,
      );
      return { ...r, headers };
    });
  }

  async function executeReplay() {
    if (!replay) return;
    setReplaying(true);
    setReplayError(null);
    setResponse(null);

    const headersRecord: Record<string, string> = {};
    for (const h of replay.headers)
      if (h.key.trim()) headersRecord[h.key.trim()] = h.value;

    const payload: ReplayPayload = {
      method: replay.method,
      path: replay.path,
      headers: headersRecord,
      body:
        METHODS_WITH_BODY.has(replay.method) && replay.body
          ? replay.body
          : null,
    };

    try {
      const res = await api.replayRequest(payload);
      setResponse(res);
    } catch (err) {
      setReplayError(err instanceof Error ? err.message : "Request failed");
    } finally {
      setReplaying(false);
    }
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
          Live request inspector — last 1 000 requests, newest first. Click a
          row to see full headers &amp; body.
        </p>
        <button onClick={handleClear} disabled={entries.length === 0}>
          Clear Log
        </button>
      </div>

      {entries.length === 0 ? (
        <div className="empty-state">
          No requests yet — send some traffic through the tunnel.
        </div>
      ) : (
        <div style={{ overflowX: "auto", width: "100%" }}>
        <table style={{ minWidth: 700 }}>
          <thead>
            <tr>
              <th style={{ width: 16 }}></th>
              {/* expand toggle */}
              <th>Time</th>
              <th>Method</th>
              <th>Path</th>
              <th>Status</th>
              <th>Duration</th>
              <th>Annotations</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {entries.map((e) => {
              const isExpanded = expandedId === e.requestId;
              return (
                <>
                  <tr
                    key={e.requestId}
                    onClick={() => toggleExpand(e.requestId)}
                    style={{
                      cursor: "pointer",
                      background: isExpanded ? "var(--surface2)" : undefined,
                    }}
                  >
                    <td
                      style={{
                        textAlign: "center",
                        fontSize: 10,
                        color: "var(--text-muted)",
                        userSelect: "none",
                      }}
                    >
                      <span
                        style={{
                          display: "inline-block",
                          transition: "transform 0.15s",
                          transform: isExpanded ? "rotate(90deg)" : "none",
                        }}
                      >
                        ▶
                      </span>
                    </td>

                    <td
                      style={{
                        color: "var(--text-muted)",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {formatTime(e.timestamp)}
                    </td>
                    <td>
                      <span className="badge badge-muted">{e.method}</span>
                    </td>
                    <td>
                      <code style={{ wordBreak: "break-all" }}>{e.path}</code>
                      {e.resolvedTargetUrl && (
                        <span
                          style={{
                            color: "var(--text-muted)",
                            fontSize: 11,
                            display: "block",
                          }}
                        >
                          → {e.resolvedTargetUrl}
                        </span>
                      )}
                    </td>
                    <td>
                      <span className={`badge ${statusClass(e.statusCode)}`}>
                        {e.statusCode}
                      </span>
                    </td>
                    <td
                      style={{
                        color:
                          e.durationMs > 1000
                            ? "var(--yellow)"
                            : "var(--text-muted)",
                      }}
                    >
                      {e.durationMs}ms
                    </td>
                    <td>
                      <div
                        style={{ display: "flex", gap: 4, flexWrap: "wrap" }}
                      >
                        {e.appliedChaosRule && (
                          <span
                            className="badge badge-red"
                            title="Chaos rule applied"
                          >
                            ⚡ {e.appliedChaosRule}
                          </span>
                        )}
                        {e.appliedMockRule && (
                          <span
                            className="badge badge-purple"
                            title="Mock rule applied"
                          >
                            🎭 {e.appliedMockRule}
                          </span>
                        )}
                      </div>
                    </td>
                    <td>
                      <button
                        onClick={(ev) => openReplay(e, ev)}
                        title="Replay this request with edits"
                      >
                        ↩ Replay
                      </button>
                    </td>
                  </tr>

                  {isExpanded && (
                    <tr
                      key={`${e.requestId}-detail`}
                      onClick={(ev) => ev.stopPropagation()}
                    >
                      <td
                        colSpan={8}
                        style={{
                          background: "var(--surface2)",
                          borderBottom: "2px solid var(--border)",
                          padding: "12px 20px",
                        }}
                      >
                        {/* Headers grid */}
                        <div
                          style={{
                            display: "grid",
                            gridTemplateColumns: "1fr 1fr",
                            gap: 20,
                            marginBottom: 12,
                          }}
                        >
                          <div>
                            <div
                              style={{
                                fontSize: 11,
                                fontWeight: 700,
                                textTransform: "uppercase",
                                letterSpacing: "0.6px",
                                color: "var(--text-muted)",
                                marginBottom: 8,
                              }}
                            >
                              Request Headers
                              {e.requestHeaders && (
                                <span
                                  style={{ fontWeight: 400, marginLeft: 6 }}
                                >
                                  ({Object.keys(e.requestHeaders).length})
                                </span>
                              )}
                            </div>
                            <HeaderTable
                              headers={e.requestHeaders}
                              keyColor="var(--accent-h)"
                              emptyLabel="No headers captured"
                            />
                          </div>

                          <div>
                            <div
                              style={{
                                fontSize: 11,
                                fontWeight: 700,
                                textTransform: "uppercase",
                                letterSpacing: "0.6px",
                                color: "var(--text-muted)",
                                marginBottom: 8,
                              }}
                            >
                              Response Headers
                              {e.responseHeaders && (
                                <span
                                  style={{ fontWeight: 400, marginLeft: 6 }}
                                >
                                  ({Object.keys(e.responseHeaders).length})
                                </span>
                              )}
                            </div>
                            <HeaderTable
                              headers={e.responseHeaders}
                              keyColor="var(--green)"
                              emptyLabel="No headers captured"
                            />
                          </div>
                        </div>

                        {e.requestBodyPreview && (
                          <div>
                            <div
                              style={{
                                fontSize: 11,
                                fontWeight: 700,
                                textTransform: "uppercase",
                                letterSpacing: "0.6px",
                                color: "var(--text-muted)",
                                marginBottom: 6,
                              }}
                            >
                              Request Body
                              {e.requestContentLength != null && (
                                <span
                                  style={{ fontWeight: 400, marginLeft: 6 }}
                                >
                                  {e.requestContentLength > 2048
                                    ? `(first 2 KB of ${e.requestContentLength} bytes)`
                                    : `(${e.requestContentLength} bytes)`}
                                </span>
                              )}
                            </div>
                            <pre
                              style={{
                                background: "var(--bg)",
                                border: "1px solid var(--border)",
                                borderRadius: 4,
                                padding: "8px 10px",
                                fontSize: 11,
                                fontFamily: "var(--mono)",
                                overflowX: "auto",
                                overflowY: "auto",
                                maxHeight: 160,
                                margin: 0,
                                whiteSpace: "pre-wrap",
                                wordBreak: "break-word",
                                color: "var(--text)",
                              }}
                            >
                              {tryPrettyJson(e.requestBodyPreview)}
                            </pre>
                          </div>
                        )}
                      </td>
                    </tr>
                  )}
                </>
              );
            })}
          </tbody>
        </table>
        </div>
      )}

      {replayEntry && replay && (
        <div
          className="modal-backdrop"
          onClick={(e) => e.target === e.currentTarget && closeReplay()}
        >
          <div className="modal" style={{ width: 700 }}>
            <div className="modal-header">
              <div>
                <h3>Replay Request</h3>
                <span style={{ fontSize: 11, color: "var(--text-muted)" }}>
                  Original: {replayEntry.method} {replayEntry.path} →{" "}
                  {replayEntry.statusCode}
                </span>
              </div>
              <button className="icon" onClick={closeReplay}>
                ✕
              </button>
            </div>

            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <div className="form-row">
                <div className="form-group" style={{ flex: "0 0 120px" }}>
                  <label>Method</label>
                  <select
                    value={replay.method}
                    onChange={(e) => setField("method", e.target.value)}
                  >
                    {[
                      "GET",
                      "POST",
                      "PUT",
                      "DELETE",
                      "PATCH",
                      "OPTIONS",
                      "HEAD",
                    ].map((m) => (
                      <option key={m} value={m}>
                        {m}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="form-group" style={{ flex: 1 }}>
                  <label>Path</label>
                  <input
                    value={replay.path}
                    onChange={(e) => setField("path", e.target.value)}
                    placeholder="/api/users?page=1"
                    style={{ fontFamily: "var(--mono)" }}
                  />
                </div>
              </div>

              <div className="form-group">
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    marginBottom: 6,
                  }}
                >
                  <label style={{ margin: 0 }}>Request Headers</label>
                  <button
                    onClick={addHeader}
                    style={{ padding: "2px 10px", fontSize: 12 }}
                  >
                    + Add
                  </button>
                </div>
                {replay.headers.length === 0 ? (
                  <span style={{ color: "var(--text-muted)", fontSize: 12 }}>
                    No headers
                  </span>
                ) : (
                  replay.headers.map((h, i) => (
                    <div
                      key={i}
                      style={{ display: "flex", gap: 6, marginBottom: 4 }}
                    >
                      <input
                        value={h.key}
                        onChange={(e) => updateHeader(i, "key", e.target.value)}
                        placeholder="Header-Name"
                        style={{ flex: "0 0 160px" }}
                      />
                      <input
                        value={h.value}
                        onChange={(e) =>
                          updateHeader(i, "value", e.target.value)
                        }
                        placeholder="value"
                        style={{ flex: 1 }}
                      />
                      <button
                        className="icon danger"
                        onClick={() => removeHeader(i)}
                        title="Remove header"
                      >
                        ✕
                      </button>
                    </div>
                  ))
                )}
              </div>

              {METHODS_WITH_BODY.has(replay.method) && (
                <div className="form-group">
                  <label>Request Body</label>
                  <textarea
                    value={replay.body}
                    onChange={(e) => setField("body", e.target.value)}
                    placeholder='{"key": "value"}'
                    rows={5}
                  />
                </div>
              )}

              {/* API-level replay error */}
              {replayError && (
                <div className="api-error">
                  <span className="api-error-icon">✕</span>
                  <span>{replayError}</span>
                </div>
              )}

              {response && (
                <div
                  style={{
                    borderTop: "1px solid var(--border)",
                    paddingTop: 12,
                    display: "flex",
                    flexDirection: "column",
                    gap: 10,
                  }}
                >
                  <div
                    style={{ display: "flex", alignItems: "center", gap: 10 }}
                  >
                    <span
                      className={`badge ${statusClass(response.statusCode)}`}
                      style={{ fontSize: 14 }}
                    >
                      {response.statusCode}
                    </span>
                    <span style={{ color: "var(--text-muted)", fontSize: 12 }}>
                      {response.durationMs}ms
                    </span>
                    <button
                      style={{
                        marginLeft: "auto",
                        fontSize: 12,
                        padding: "2px 10px",
                      }}
                      onClick={() => setShowRespHeaders((v) => !v)}
                    >
                      {showRespHeaders ? "Hide" : "Show"} Response Headers (
                      {Object.keys(response.headers).length})
                    </button>
                  </div>

                  {showRespHeaders && (
                    <div
                      style={{
                        background: "var(--surface2)",
                        borderRadius: 6,
                        padding: "8px 12px",
                        fontSize: 12,
                        fontFamily: "var(--mono)",
                      }}
                    >
                      {Object.entries(response.headers).map(([k, v]) => (
                        <div
                          key={k}
                          style={{ display: "flex", gap: 8, marginBottom: 2 }}
                        >
                          <span
                            style={{ color: "var(--accent-h)", minWidth: 160 }}
                          >
                            {k}
                          </span>
                          <span
                            style={{
                              color: "var(--text-muted)",
                              wordBreak: "break-all",
                            }}
                          >
                            {v}
                          </span>
                        </div>
                      ))}
                    </div>
                  )}

                  <div>
                    <label style={{ marginBottom: 4 }}>Response Body</label>
                    <pre
                      style={{
                        background: "var(--surface2)",
                        border: "1px solid var(--border)",
                        borderRadius: 6,
                        padding: "10px 12px",
                        fontSize: 12,
                        fontFamily: "var(--mono)",
                        overflowX: "auto",
                        overflowY: "auto",
                        maxHeight: 240,
                        margin: 0,
                        whiteSpace: "pre-wrap",
                        wordBreak: "break-word",
                        color: "var(--text)",
                      }}
                    >
                      {tryPrettyJson(response.body) || (
                        <span style={{ color: "var(--text-muted)" }}>
                          (empty)
                        </span>
                      )}
                    </pre>
                  </div>
                </div>
              )}
            </div>

            <div className="modal-footer">
              <button onClick={closeReplay}>Close</button>
              <button
                className="primary"
                onClick={executeReplay}
                disabled={replaying}
                style={{ minWidth: 120 }}
              >
                {replaying ? "⏳ Sending…" : "↩ Send Request"}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
