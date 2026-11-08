import { useEffect, useId, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react'

export type Option = { value: string; label: string }

// custom dropdown (button + listbox) so the open list can use the site colors; a native select's list cannot be styled
export default function Select({ value, options, onChange, label, disabled }: {
  value: string
  options: Option[]
  onChange: (value: string) => void
  label: string
  disabled?: boolean
}) {
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const [pos, setPos] = useState<CSSProperties>({})
  const box = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const list = useRef<HTMLUListElement>(null)
  const id = useId()

  const selected = options.findIndex((o) => o.value === value)

  function show() {
    const r = button.current!.getBoundingClientRect()
    // fixed so a scrolling table or dialog cannot clip it; opens upward when there is no room below
    const height = Math.min(240, options.length * 32 + 10) // rows are 32 tall plus padding; the list is not rendered yet
    const up = window.innerHeight - r.bottom < height && r.top > window.innerHeight - r.bottom
    setPos({ left: r.left, minWidth: r.width, ...(up ? { bottom: window.innerHeight - r.top + 4 } : { top: r.bottom + 4 }) })
    setActive(Math.max(selected, 0))
    setOpen(true)
  }

  function close(refocus: boolean) {
    setOpen(false)
    if (refocus) button.current?.focus()
  }

  function pick(i: number) {
    if (options[i].value !== value) onChange(options[i].value)
    close(true)
  }

  useEffect(() => {
    if (!open) return
    list.current?.focus()
    const onDown = (e: MouseEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false) }
    // page scrolled or resized under it: close, and keep focus on the button so it is not lost
    const onMove = (e: Event) => {
      if (list.current?.contains(e.target as Node)) return
      if (list.current?.contains(document.activeElement)) button.current?.focus({ preventScroll: true })
      setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    window.addEventListener('scroll', onMove, true)
    window.addEventListener('resize', onMove)
    return () => {
      document.removeEventListener('mousedown', onDown)
      window.removeEventListener('scroll', onMove, true)
      window.removeEventListener('resize', onMove)
    }
  }, [open])

  useEffect(() => {
    // keep the active row in view by moving the list's own scroll (scrollIntoView would scroll the page and close it)
    const ul = list.current
    const li = document.getElementById(`${id}-${active}`)
    if (!open || !ul || !li) return
    if (li.offsetTop < ul.scrollTop) ul.scrollTop = li.offsetTop
    else if (li.offsetTop + li.offsetHeight > ul.scrollTop + ul.clientHeight) ul.scrollTop = li.offsetTop + li.offsetHeight - ul.clientHeight
  }, [open, active, id])

  useEffect(() => {
    if (disabled) setOpen(false)
  }, [disabled])

  function onButtonKey(e: KeyboardEvent) {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      show()
    }
  }

  function onListKey(e: KeyboardEvent) {
    const last = options.length - 1
    if (e.key === 'ArrowDown') setActive((a) => Math.min(a + 1, last))
    else if (e.key === 'ArrowUp') setActive((a) => Math.max(a - 1, 0))
    else if (e.key === 'Home') setActive(0)
    else if (e.key === 'End') setActive(last)
    else if (e.key === 'Enter' || e.key === ' ') pick(active)
    else if (e.key === 'Escape') close(true)
    else if (e.key === 'Tab') setOpen(false)
    else if (e.key.length === 1) {
      // type-ahead: next option starting with the letter
      const c = e.key.toLowerCase()
      const from = [...options.slice(active + 1), ...options.slice(0, active + 1)]
      const hit = from.find((o) => o.label.toLowerCase().startsWith(c))
      if (hit) setActive(options.indexOf(hit))
      return
    } else return
    e.preventDefault()
    e.stopPropagation() // Escape must not also close a dialog around the select
  }

  return (
    <div className="select" ref={box}>
      <button
        type="button"
        ref={button}
        className="select-button"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={`${label}: ${options[selected]?.label ?? ''}`}
        disabled={disabled}
        onClick={() => (open ? close(false) : show())}
        onKeyDown={onButtonKey}
      >
        {options[selected]?.label ?? ''}
      </button>
      {open && (
        <ul
          ref={list}
          className="select-list"
          style={pos}
          role="listbox"
          tabIndex={-1}
          aria-label={label}
          aria-activedescendant={`${id}-${active}`}
          onKeyDown={onListKey}
        >
          {options.map((o, i) => (
            <li
              key={o.value}
              id={`${id}-${i}`}
              role="option"
              aria-selected={i === selected}
              className={i === active ? 'active' : ''}
              onMouseMove={() => setActive(i)}
              onClick={(e) => { e.preventDefault(); pick(i) }} // a label around the select would otherwise click the button again
            >
              {o.label}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
