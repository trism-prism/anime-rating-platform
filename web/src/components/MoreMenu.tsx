import { useEffect, useRef, useState, type KeyboardEvent } from 'react'

export interface MoreItem {
  label: string
  onSelect: () => void
  disabled?: boolean
}

// "More" button with a menu of actions: arrow keys move, Escape closes and returns focus to the button, every item looks the same
export default function MoreMenu({ items, disabled }: { items: MoreItem[]; disabled?: boolean }) {
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)

  const entries = () => Array.from(box.current?.querySelectorAll<HTMLElement>('[role="menuitem"]:not(:disabled)') ?? [])

  useEffect(() => {
    if (!open) return
    entries()[0]?.focus()
    const onDown = (e: MouseEvent) => {
      if (!box.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  function onKeyDown(e: KeyboardEvent) {
    if (!open) return
    if (e.key === 'Escape') {
      setOpen(false)
      button.current?.focus()
    } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Home' || e.key === 'End') {
      e.preventDefault()
      const list = entries()
      const i = list.indexOf(document.activeElement as HTMLElement)
      const next = e.key === 'Home' ? 0 : e.key === 'End' ? list.length - 1 : e.key === 'ArrowDown' ? i + 1 : i - 1
      list[(next + list.length) % list.length]?.focus()
    }
  }

  function choose(item: MoreItem) {
    setOpen(false)
    button.current?.focus() // a dialog opened by the item hands focus back here when it closes
    item.onSelect()
  }

  return (
    <div
      className="more"
      ref={box}
      onKeyDown={onKeyDown}
      onBlur={(e) => { if (e.relatedTarget && !e.currentTarget.contains(e.relatedTarget)) setOpen(false) }}
    >
      <button ref={button} type="button" className="more-button" aria-haspopup="menu" aria-expanded={open} disabled={disabled} onClick={() => setOpen(!open)}>More</button>
      {open && (
        <div className="menu more-menu" role="menu" aria-label="More actions">
          {items.map((item) => (
            <button key={item.label} type="button" role="menuitem" disabled={item.disabled} onClick={() => choose(item)}>{item.label}</button>
          ))}
        </div>
      )}
    </div>
  )
}
