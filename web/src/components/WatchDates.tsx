import { useEffect, useId, useRef, useState } from 'react'
import { api } from '../api'
import { longDate } from '../format'
import type { WatchEntry } from '../types'

type Dates = { started: string; finished: string }

const SAVE_DELAY = 700

// "7 November 2026"; noon, so a date-only value never slips a day with the time zone
const long = (iso: string) => (iso ? longDate(`${iso}T12:00:00`) : '')

// started / finished dates of one list entry, saved a moment after the last change; the server checks the dates
export default function WatchDates({ entry, onSaved, onError }: {
  entry: WatchEntry
  onSaved: (titleId: number, startedOn: string | null, finishedOn: string | null) => void
  onError: (message: string) => void
}) {
  const [dates, setDates] = useState<Dates>({ started: entry.startedOn ?? '', finished: entry.finishedOn ?? '' })
  const [state, setState] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [error, setError] = useState('')
  const timer = useRef<number | undefined>(undefined)
  const latest = useRef(dates) // what the timer saves, also on unmount
  const busy = useRef(false) // one request at a time; a change made meanwhile waits in `queued`
  const queued = useRef<Dates | null>(null)
  const props = useRef({ entry, onSaved, onError })
  const id = useId()

  useEffect(() => { props.current = { entry, onSaved, onError } })

  async function save(d: Dates) {
    if (busy.current) { queued.current = d; return }
    busy.current = true
    const { entry: e, onSaved: saved, onError: failed } = props.current
    setState('saving')
    setError('')
    try {
      const body = { startedOn: d.started || null, finishedOn: d.finished || null, rewatchCount: e.rewatchCount }
      await api(`/me/watchlist/${e.titleId}/dates`, { method: 'PUT', body: JSON.stringify(body) })
      saved(e.titleId, body.startedOn, body.finishedOn)
      setState('saved')
    } catch (err) {
      setError((err as Error).message)
      setState('idle')
      failed((err as Error).message)
    }
    busy.current = false
    const next = queued.current
    queued.current = null
    if (next) void save(next)
  }

  function change(next: Dates) {
    setDates(next)
    latest.current = next
    setState('idle')
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => { timer.current = undefined; void save(next) }, SAVE_DELAY)
  }

  // closing the row must not lose a change that is still waiting
  useEffect(() => () => {
    if (timer.current !== undefined) { window.clearTimeout(timer.current); void save(latest.current) }
  }, [])

  return (
    <div className="mylist-dates">
      <div className="mylist-field">
        <label htmlFor={`${id}-s`}>Started</label>
        <input id={`${id}-s`} type="date" value={dates.started} max={dates.finished || undefined}
          onChange={(e) => change({ ...dates, started: e.target.value })} />
        <span className="hint">{long(dates.started)}</span>
      </div>
      <div className="mylist-field">
        <label htmlFor={`${id}-f`}>Finished</label>
        <input id={`${id}-f`} type="date" value={dates.finished} min={dates.started || undefined}
          onChange={(e) => change({ ...dates, finished: e.target.value })} />
        <span className="hint">{long(dates.finished)}</span>
      </div>
      <p className={error ? 'error' : 'hint'} role="status">
        {error || (state === 'saving' ? 'Saving' : state === 'saved' ? 'Saved' : '')}
      </p>
    </div>
  )
}
