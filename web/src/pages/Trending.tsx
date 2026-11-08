import { useEffect, useState } from 'react'
import { api } from '../api'
import SeriesCard from '../components/SeriesCard'
import type { TitleType, TrendingItem } from '../types'
import { useTitle } from '../useTitle'
import pageMeta from '../pageMeta.json'

export default function Trending() {
  useTitle(pageMeta['/trending'].title)
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
      <p className="muted">Updated from AniList trending</p>
      <div className="filters">
        {(['Anime', 'Manga'] as const).map((t) => (
          <button key={t} className={type === t ? 'on' : ''} aria-pressed={type === t} onClick={() => setType(t)}>{t}</button>
        ))}
      </div>
      {error && <p className="error">{error}</p>}
      {loading && !error && <p className="muted">Loading</p>}
      {!loading && items.list.length === 0 && <p className="muted">Nothing trending right now.</p>}
      {!loading && (
        <>
        <h2 className="sr-only">Trending {type.toLowerCase()}</h2>
        <div className="grid">
          {items.list.map((t) => <SeriesCard key={t.id} id={t.id} name={t.name} coverUrl={t.coverUrl} type={t.type} average={t.averageOverall} />)}
        </div>
        </>
      )}
    </div>
  )
}
