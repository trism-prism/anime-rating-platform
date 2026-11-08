import { useEffect, useId, useRef, type ReactNode } from 'react'

// native modal dialog: focus trap, Escape and backdrop come from the browser
export default function ConfirmDialog({ open, title, children, confirmLabel, busy, onConfirm, onCancel }: {
  open: boolean
  title: string
  children?: ReactNode
  confirmLabel: string
  busy: boolean
  onConfirm: () => void
  onCancel: () => void
}) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()

  useEffect(() => {
    const d = ref.current
    if (!d) return
    if (open && !d.open) d.showModal()
    if (!open && d.open) d.close()
  }, [open])

  // if the owner unmounts while it is open, close it so focus goes back to where it was
  useEffect(() => {
    const d = ref.current
    return () => { if (d?.open) d.close() }
  }, [])

  return (
    <dialog ref={ref} aria-labelledby={titleId} onCancel={(e) => { e.preventDefault(); if (!busy) onCancel() }}>
      <h3 id={titleId}>{title}</h3>
      <div>{children}</div>
      <div className="row row-end">
        <button type="button" onClick={onCancel} disabled={busy}>Cancel</button>
        <button type="button" onClick={onConfirm} disabled={busy} className="on">{busy ? 'Working' : confirmLabel}</button>
      </div>
    </dialog>
  )
}
