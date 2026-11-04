import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { device } from '../device'

type Row = { type: string; ip: string; userAgent: string; at: string }

const LABELS: Record<string, string> = {
  login: 'Signed in',
  'locked-out': 'Account locked after too many failed attempts',
  logout: 'Signed out',
  'password-changed': 'Password changed',
  'password-reset': 'Password reset',
  'email-change-requested': 'Email change requested',
  'email-changed': 'Email changed',
  '2fa-enabled': 'Two-factor authentication turned on',
  '2fa-disabled': 'Two-factor authentication turned off',
  'recovery-codes-regenerated': 'New recovery codes made',
  'session-revoked': 'A device was signed out',
  'other-sessions-revoked': 'All other devices were signed out',
}

// the latest 50 security events of this account
export default function Activity() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    api<Row[]>('/me/activity').then((r) => { setRows(r); setError('') }).catch((e: Error) => setError(e.message))
  }, [])
  useEffect(load, [load])

  return (
    <>
      {error && <p className="error" role="alert">{error}</p>}
      {rows === null && !error && <p className="muted">Loading...</p>}
      {rows?.length === 0 && <p className="muted">Nothing yet.</p>}
      {rows?.map((r, i) => (
        <div key={i} className="list-row">
          <span className="list-name">
            {LABELS[r.type] ?? r.type}
            <br />
            <span className="muted hint">{new Date(r.at).toLocaleString()} · {r.ip} · {device(r.userAgent)}</span>
          </span>
        </div>
      ))}
      <button type="button" onClick={load}>Refresh</button>
    </>
  )
}
