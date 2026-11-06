import { useEffect, useState } from 'react'
import { api } from '../api'
import { useAuth } from '../auth'
import { STATUSES, type WatchEntry, type WatchStatus } from '../types'

export default function ListForm({ titleId }: { titleId: number }) {
  const { user } = useAuth()
  const [status, setStatus] = useState<WatchStatus>('Planning')
  const [progress, setProgress] = useState(0)
  const [inList, setInList] = useState(false)
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!user) return
    let stale = false
    api<WatchEntry[]>('/me/watchlist')
      .then((list) => {
        const e = list.find((x) => x.titleId === titleId)
        if (stale || !e) return
        setStatus(e.status)
        setProgress(e.progress)
        setInList(true)
      })
      .catch(() => {})
    return () => {
      stale = true
    }
  }, [titleId, user])

  if (!user) return null

  async function save() {
    setMsg('')
    setError('')
    try {
      await api(`/me/watchlist/${titleId}`, { method: 'PUT', body: JSON.stringify({ status, progress }) })
      setInList(true)
      setMsg('Saved to your list')
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <div className="row">
      <select value={status} onChange={(e) => setStatus(e.target.value as WatchStatus)}>
        {STATUSES.map((s) => <option key={s}>{s}</option>)}
      </select>
      <input
        type="number"
        min={0}
        className="num"
        aria-label="Progress"
        value={progress}
        onChange={(e) => setProgress(Math.max(0, Number(e.target.value) || 0))}
      />
      <button onClick={save}>{inList ? 'Update list' : 'Add to my list'}</button>
      {msg && <span className="muted">{msg}</span>}
      {error && <span className="error">{error}</span>}
    </div>
  )
}
