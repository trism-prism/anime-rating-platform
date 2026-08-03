import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'

export const Pencil = () => (
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d="M12 20h9" /><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z" />
  </svg>
)

// shows `display` with an Edit button; Edit swaps in a form with `field(value, setValue)`, Save runs onSave
export default function InlineEdit({ label, value, display, empty, field, hint, onSave }: {
  label: string
  empty?: string // when set and the value is empty, a click-to-fill field with this text replaces display and the pencil
  value: string
  display: ReactNode
  field: (v: string, set: (v: string) => void) => ReactNode
  hint: (v: string) => string
  onSave: (v: string) => Promise<void>
}) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(value)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const edit = useRef<HTMLButtonElement>(null)
  const form = useRef<HTMLFormElement>(null)
  const was = useRef(false)

  // focus the field when editing starts, the Edit button when it ends
  useEffect(() => {
    if (editing) form.current?.querySelector<HTMLElement>('input, textarea')?.focus()
    else if (was.current) edit.current?.focus()
    was.current = editing
  }, [editing])

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setBusy(true)
    try {
      await onSave(draft)
      setEditing(false)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (!editing) {
    const start = () => { setDraft(value); setError(''); setEditing(true) }
    if (empty && !value) {
      return <button ref={edit} type="button" className="link-button field-empty" onClick={start}>{empty}</button>
    }
    return (
      <div className="inline-edit">
        {display}
        <button ref={edit} type="button" className="edit-pencil" aria-label={`Edit ${label}`} onClick={start}><Pencil /></button>
      </div>
    )
  }
  return (
    <form ref={form} className="settings-form" onSubmit={submit}>
      <label>{label}{field(draft, setDraft)}</label>
      <p className="muted hint">{hint(draft)}</p>
      {error && <p className="error" role="alert">{error}</p>}
      <div className="row">
        <button type="submit" disabled={busy}>Save</button>
        <button type="button" disabled={busy} onClick={() => setEditing(false)}>Cancel</button>
      </div>
    </form>
  )
}
