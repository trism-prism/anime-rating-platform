import { useEffect, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import AudienceSelect from '../components/AudienceSelect'
import type { Audience, ListSummary } from '../types'

export default function MyLists() {
  const { user } = useAuth()
  const [lists, setLists] = useState<ListSummary[] | null>(null)
  const [name, setName] = useState('')
  const [audience, setAudience] = useState<Audience>('Public')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!user) return
    api<ListSummary[]>('/me/lists').then(setLists).catch((e: Error) => setError(e.message))
  }, [user])

  if (!user) return <Navigate to="/login" replace />

  async function create(e: React.FormEvent) {
    e.preventDefault()
    setError('')
    try {
      const made = await api<ListSummary>('/me/lists', { method: 'POST', body: JSON.stringify({ name, audience }) })
      setLists((l) => [...(l ?? []), made])
      setName('')
    } catch (err) {
      setError((err as Error).message)
    }
  }

  async function changeAudience(l: ListSummary, a: Audience) {
    setError('')
    try {
      await api(`/me/lists/${l.id}`, { method: 'PUT', body: JSON.stringify({ name: l.name, audience: a }) })
      setLists((all) => all && all.map((x) => (x.id === l.id ? { ...x, audience: a } : x)))
    } catch (err) {
      setError((err as Error).message)
    }
  }

  async function remove(l: ListSummary) {
    if (!window.confirm(`Delete the list "${l.name}"?`)) return
    setError('')
    try {
      await api(`/me/lists/${l.id}`, { method: 'DELETE' })
      setLists((all) => all && all.filter((x) => x.id !== l.id))
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <div>
      <h1>My lists</h1>
      <form className="row" onSubmit={create}>
        <input value={name} maxLength={60} placeholder="New list name" aria-label="New list name" onChange={(e) => setName(e.target.value)} />
        <AudienceSelect label="Visibility of the new list" value={audience} onChange={setAudience} />
        <button type="submit" disabled={!name.trim()}>Create list</button>
      </form>
      {error && <p className="error">{error}</p>}
      {!lists && !error && <p className="muted">Loading</p>}
      {lists && lists.length === 0 && <p className="muted">No lists yet.</p>}
      {lists?.map((l) => (
        <div key={l.id} className="list-row">
          <Link to={`/lists/${l.id}`} className="list-name">{l.name}</Link>
          <span className="muted">{l.itemCount} {l.itemCount === 1 ? 'item' : 'items'}</span>
          <AudienceSelect label={`Visibility of ${l.name}`} value={l.audience ?? 'Public'} onChange={(a) => changeAudience(l, a)} />
          <button onClick={() => remove(l)}>Delete</button>
        </div>
      ))}
    </div>
  )
}
