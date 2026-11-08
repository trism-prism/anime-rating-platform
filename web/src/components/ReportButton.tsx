import { useState } from 'react'
import { api, ApiError } from '../api'
import ConfirmDialog from './ConfirmDialog'
import Select from './Select'

const REASONS = ['Spam', 'Harassment', 'Hate', 'Spoiler', 'Other'] as const
const MAX = 500

// the report dialog; it exists only while open. onSent fires once the report is in (or was already filed)
export function ReportDialog({ path, what, open, onClose, onSent }: { path: string; what: string; open: boolean; onClose: () => void; onSent: () => void }) {
  const [reason, setReason] = useState<(typeof REASONS)[number]>('Spam')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function send() {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await api(path, { method: 'POST', body: JSON.stringify({ reason, note }) })
      onSent()
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) onSent() // already reported
      else setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (!open) return null
  return (
    <ConfirmDialog open title={`Report this ${what}`} confirmLabel="Send report" busy={busy} onConfirm={send} onCancel={onClose}>
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
  )
}

// "Report" link with the dialog; path is the report route of the review, reply or profile
export default function ReportButton({ path, what }: { path: string; what: string }) {
  const [open, setOpen] = useState(false)
  const [done, setDone] = useState(false)
  if (done) return <span className="muted">Reported</span>
  return (
    <>
      <button className="link-button" onClick={() => setOpen(true)} aria-label={`Report this ${what}`}>Report</button>
      <ReportDialog path={path} what={what} open={open} onClose={() => setOpen(false)} onSent={() => setDone(true)} />
    </>
  )
}
