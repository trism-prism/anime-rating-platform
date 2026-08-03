import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import type { ListSummary } from '../types'
import Select from './Select'

// add this title or series to one of the signed-in user's custom lists
export default function AddToList({ titleId, seriesId }: { titleId?: number; seriesId?: number }) {
  const { user } = useAuth()
  const [lists, setLists] = useState<ListSummary[] | null>(null)
  const [picked, setPicked] = useState(0)
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!user) return
    api<ListSummary[]>('/me/lists').then(setLists).catch(() => {})
  }, [user])

  if (!user || !lists) return null
  if (lists.length === 0) return <><h2>Custom lists</h2><p className="muted">You have no custom lists yet. <Link to="/my-list" className="link">Make one</Link>.</p></>

  async function add() {
    setMsg('')
    setError('')
    try {
      await api(`/me/lists/${picked}/items`, { method: 'POST', body: JSON.stringify({ titleId: titleId ?? null, seriesId: seriesId ?? null }) })
      setMsg('Added')
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <>
    <h2>Custom lists</h2>
    <div className="row">
      <Select
        label="Custom list"
        value={String(picked)}
        options={[{ value: '0', label: 'Pick a list' }, ...lists.map((l) => ({ value: String(l.id), label: l.name }))]}
        onChange={(v) => setPicked(Number(v))}
      />
      <button disabled={picked === 0} onClick={add}>Add to list</button>
      {msg && <span className="muted">{msg}</span>}
      {error && <span className="error">{error}</span>}
    </div>
    </>
  )
}
