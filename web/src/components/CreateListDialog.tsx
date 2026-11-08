import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { api } from '../api'
import type { Audience, ListSummary } from '../types'
import AudienceSelect from './AudienceSelect'

// native modal dialog (like ConfirmDialog): Escape closes it, focus starts on the name field
export default function CreateListDialog({ open, onClose, onCreated }: {
  open: boolean
  onClose: () => void
  onCreated: (list: ListSummary) => void
}) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  const [name, setName] = useState('')
  const [audience, setAudience] = useState<Audience>('Public')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    const d = ref.current
    if (!d) return
    if (open && !d.open) {
      setName('')
      setAudience('Public')
      setError('')
      d.showModal()
    }
    if (!open && d.open) d.close()
  }, [open])

  async function create(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      onCreated(await api<ListSummary>('/me/lists', { method: 'POST', body: JSON.stringify({ name: name.trim(), audience }) }))
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <dialog ref={ref} aria-labelledby={titleId} onCancel={(e) => { e.preventDefault(); if (!busy) onClose() }}>
      <h3 id={titleId}>Create list</h3>
      <form className="stack" onSubmit={create}>
        <input value={name} maxLength={60} placeholder="List name" aria-label="List name" onChange={(e) => setName(e.target.value)} />
        <div className="row">
          <span>Visibility</span>
          <AudienceSelect label="Visibility of the new list" value={audience} onChange={setAudience} />
        </div>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="row row-end">
          <button type="button" onClick={onClose} disabled={busy}>Cancel</button>
          <button type="submit" disabled={busy || !name.trim()}>{busy ? 'Creating' : 'Create'}</button>
        </div>
      </form>
    </dialog>
  )
}
