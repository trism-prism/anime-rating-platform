import { useEffect, useState } from 'react'
import { api } from '../api'
import { useAuth } from '../auth'
import { STATUS_OPTIONS, type WatchEntry, type WatchStatus } from '../types'
import Select from './Select'

export default function ListForm({ titleId }: { titleId: number }) {
  const { user } = useAuth()
  const [status, setStatus] = useState<WatchStatus>('Planning')
  const [progress, setProgress] = useState(0)
  const [inList, setInList] = useState(false)
  const [loaded, setLoaded] = useState(false) // stored progress is known, so Save can't overwrite it
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!user) return
    let stale = false
    api<WatchEntry[]>('/me/watchlist')
      .then((list) => {
        const e = list.find((x) => x.titleId === titleId)
        if (stale) return
        setLoaded(true)
        if (!e) return
        setStatus(e.status)
        setProgress(e.progress)
        setInList(true)
      })
      .catch((err: Error) => !stale && setError(err.message))
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
      setMsg('Saved to My List')
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <div className="row">
      <Select label="Watch status" value={status} options={STATUS_OPTIONS} onChange={(v) => setStatus(v as WatchStatus)} />
      <button onClick={save} disabled={!loaded}>{inList ? 'Update list' : 'Add to My List'}</button>
      {msg && <span className="muted">{msg}</span>}
      {error && <span className="error">{error}</span>}
    </div>
  )
}
