import { useState } from 'react'
import { api, ApiError } from '../api'
import ConfirmDialog from './ConfirmDialog'
import Select from './Select'

const REASONS = ['Spam', 'Harassment', 'Hate', 'Spoiler', 'Other'] as const
const MAX = 500

// "Report" link with a small dialog; path is the report route of the review, reply or profile. The dialog exists only while open.
export default function ReportButton({ path, what }: { path: string; what: string }) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState<(typeof REASONS)[number]>('Spam')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [done, setDone] = useState(false)

  async function send() {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await api(path, { method: 'POST', body: JSON.stringify({ reason, note }) })
      setDone(true)
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) setDone(true) // already reported
      else setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (done) return <span className="muted">Reported</span>
  return (
    <>
      <button className="link-button" onClick={() => setOpen(true)} aria-label={`Report this ${what}`}>Report</button>
      {open && (
        <ConfirmDialog open title={`Report this ${what}`} confirmLabel="Send report" busy={busy} onConfirm={send} onCancel={() => setOpen(false)}>
          <div className="stack">
            <label>
              Reason{' '}
              <Select label="Reason" value={reason} options={REASONS.map((r) => ({ value: r, label: r }))} onChange={(v) => setReason(v as (typeof REASONS)[number])} />
            </label>
            <textarea aria-label="Note (optional)" placeholder="Note (optional)" rows={3} maxLength={MAX} value={note} onChange={(e) => setNote(e.target.value)} />
            <span className="muted count">{note.length}/{MAX}</span>
            {error && <p className="error" role="alert">{error}</p>}
          </div>
        </ConfirmDialog>
      )}
    </>
  )
}
