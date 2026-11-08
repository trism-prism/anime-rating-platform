import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import Select from '../components/Select'
import type { RankingRow } from '../types'

const CATEGORY_OPTIONS = ['overall', 'story', 'art', 'characters', 'enjoyment'].map((c) => ({ value: c, label: c[0].toUpperCase() + c.slice(1) }))
const TYPES = [{ value: '', label: 'Anime and manga' }, { value: 'Anime', label: 'Anime' }, { value: 'Manga', label: 'Manga' }]

export default function Rankings() {
  const [category, setCategory] = useState('overall')
  const [type, setType] = useState('')
  const [rows, setRows] = useState<{ key: string; list: RankingRow[] } | null>(null)
  const [error, setError] = useState('')

  const key = `${category}|${type}`

  useEffect(() => {
    let stale = false
    const qs = new URLSearchParams({ category })
    if (type) qs.set('type', type)
    api<RankingRow[]>(`/rankings?${qs}`)
      .then((list) => { if (!stale) { setRows({ key, list }); setError('') } })
      .catch((e: Error) => !stale && setError(e.message))
    return () => {
      stale = true
    }
  }, [category, type, key])

  const loading = !rows || rows.key !== key

  return (
    <div>
      <h1>Rankings</h1>
      <div className="filters">
        <Select label="Category" value={category} options={CATEGORY_OPTIONS} onChange={setCategory} />
        <Select label="Type" value={type} options={TYPES} onChange={setType} />
      </div>
      {error && <p className="error">{error}</p>}
      {loading && !error && <p className="muted">Loading</p>}
      {!loading && rows.list.length === 0 && <p className="muted">Not enough ratings yet.</p>}
      {!loading && (
        <ol className="rankings">
          {rows.list.map((r) => (
            <li key={r.id}>
              <Link to={`/series/${r.id}`} className="list-name">{r.name}</Link>
              <span className="muted">{r.type}</span>
              <strong>{r.average}</strong>
              <span className="muted"><span className="num">{r.ratingCount}</span> {r.ratingCount === 1 ? 'rating' : 'ratings'}</span>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}
