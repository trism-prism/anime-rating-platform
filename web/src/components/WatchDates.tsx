import { useState } from 'react'
import { api } from '../api'
import type { WatchEntry } from '../types'

// started / finished dates of one list entry; the server checks the dates
export default function WatchDates({ entry, onSaved }: { entry: WatchEntry; onSaved: (e: WatchEntry) => void }) {
  const [started, setStarted] = useState(entry.startedOn ?? '')
  const [finished, setFinished] = useState(entry.finishedOn ?? '')
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')

  async function save() {
    setMsg('')
    setError('')
    try {
      const body = { startedOn: started || null, finishedOn: finished || null, rewatchCount: entry.rewatchCount }
      await api(`/me/watchlist/${entry.titleId}/dates`, { method: 'PUT', body: JSON.stringify(body) })
      onSaved({ ...entry, startedOn: body.startedOn, finishedOn: body.finishedOn })
      setMsg('Saved')
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <div className="row">
      <label>Started <input type="date" value={started} onChange={(e) => setStarted(e.target.value)} /></label>
      <label>Finished <input type="date" value={finished} min={started || undefined} onChange={(e) => setFinished(e.target.value)} /></label>
      <button onClick={save}>Save dates</button>
      {msg && <span className="muted">{msg}</span>}
      {error && <span className="error">{error}</span>}
    </div>
  )
}
