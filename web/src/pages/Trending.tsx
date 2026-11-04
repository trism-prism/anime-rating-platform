import { useEffect, useState } from 'react'
import { api } from '../api'
import TitleCard from '../components/TitleCard'
import type { TitleType, TrendingItem } from '../types'

export default function Trending() {
  const [type, setType] = useState<TitleType>('Anime')
  const [items, setItems] = useState<{ type: TitleType; list: TrendingItem[] } | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let stale = false
    api<TrendingItem[]>(`/trending?type=${type}`)
      .then((list) => { if (!stale) { setItems({ type, list }); setError('') } })
      .catch((e: Error) => !stale && setError(e.message))
    return () => {
      stale = true
    }
  }, [type])

  const loading = !items || items.type !== type

  return (
    <div>
      <h1>Trending</h1>
      <div className="filters">
        {(['Anime', 'Manga'] as const).map((t) => (
          <button key={t} className={type === t ? 'on' : ''} onClick={() => setType(t)}>{t}</button>
        ))}
      </div>
      {error && <p className="error">{error}</p>}
      {loading && !error && <p className="muted">Loading...</p>}
      {!loading && items.list.length === 0 && <p className="muted">Nothing trending right now.</p>}
      {!loading && (
        <div className="grid">
          {items.list.map((t) => <TitleCard key={t.id} title={t} />)}
        </div>
      )}
    </div>
  )
}
