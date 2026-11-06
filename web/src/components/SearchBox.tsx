import { useEffect, useId, useState, type KeyboardEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api'
import type { Suggestion } from '../types'

const MIN_CHARS = 2
const DEBOUNCE_MS = 250

// text box with series typeahead (GET /search/suggest); submitting the form without a highlighted option runs a full search
export default function SearchBox({ onSubmit }: { onSubmit: (text: string) => void }) {
  const navigate = useNavigate()
  const uid = useId()
  const listId = `${uid}-list`
  const [text, setText] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const [result, setResult] = useState<{ q: string; list: Suggestion[]; failed: boolean } | null>(null)

  const q = text.trim()

  useEffect(() => {
    if (q.length < MIN_CHARS) return
    const run = { stale: false }
    const timer = setTimeout(() => {
      api<Suggestion[]>(`/search/suggest?q=${encodeURIComponent(q)}`)
        .then((list) => { if (!run.stale) { setResult({ q, list, failed: false }); setActive(-1) } })
        .catch(() => { if (!run.stale) setResult({ q, list: [], failed: true }) })
    }, DEBOUNCE_MS)
    return () => {
      run.stale = true
      clearTimeout(timer)
    }
  }, [q])

  const ready = q.length >= MIN_CHARS && result?.q === q
  const options = ready ? result.list : []
  const showPopup = open && q.length >= MIN_CHARS
  const status = !ready ? 'Searching' : result.failed ? 'Suggestions are unavailable.' : options.length === 0 ? 'No matching series.' : ''

  function choose(s: Suggestion) {
    setOpen(false)
    navigate(`/series/${s.id}`)
  }

  function onKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      if (options.length === 0) return
      e.preventDefault()
      setOpen(true)
      const step = e.key === 'ArrowDown' ? 1 : -1
      setActive((a) => (a < 0 ? (step === 1 ? 0 : options.length - 1) : (a + step + options.length) % options.length))
    } else if (e.key === 'Enter' && showPopup && active >= 0 && options[active]) {
      e.preventDefault()
      choose(options[active])
    } else if (e.key === 'Escape' && open) {
      e.preventDefault()
      setOpen(false)
      setActive(-1)
    }
  }

  return (
    <form
      className="search"
      role="search"
      onSubmit={(e) => {
        e.preventDefault()
        setOpen(false)
        onSubmit(q)
      }}
    >
      <input
        type="text"
        role="combobox"
        aria-label="Search series"
        aria-autocomplete="list"
        aria-expanded={showPopup && options.length > 0}
        aria-controls={options.length > 0 ? listId : undefined}
        aria-activedescendant={showPopup && active >= 0 ? `${uid}-opt-${active}` : undefined}
        autoComplete="off"
        placeholder="Search series"
        value={text}
        onChange={(e) => { setText(e.target.value); setOpen(true); setActive(-1) }}
        onKeyDown={onKeyDown}
        onFocus={() => setOpen(true)}
        onBlur={() => setOpen(false)}
      />
      {showPopup && (
        <div className="suggest">
          {options.length > 0 ? (
            <ul id={listId} role="listbox" aria-label="Suggestions">
              {options.map((s, i) => (
                <li
                  key={s.id}
                  id={`${uid}-opt-${i}`}
                  role="option"
                  aria-selected={i === active}
                  className={i === active ? 'on' : ''}
                  onMouseDown={(e) => e.preventDefault()} // keep focus in the input so the click lands
                  onClick={() => choose(s)}
                  onMouseMove={() => setActive(i)}
                >
                  <span>{s.name}</span>
                  <span className="muted">{s.kind}</span>
                </li>
              ))}
            </ul>
          ) : (
            <div className="suggest-status muted">{status}</div>
          )}
        </div>
      )}
      <div className="sr-only" role="status" aria-live="polite">
        {showPopup ? (options.length > 0 ? `${options.length} suggestions` : status) : ''}
      </div>
      <button type="submit">Search</button>
    </form>
  )
}
