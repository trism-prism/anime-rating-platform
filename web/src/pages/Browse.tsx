import { useEffect, useState } from 'react'
import { api } from '../api'
import TitleCard from '../components/TitleCard'
import type { TitleList } from '../types'

const PAGE_SIZE = 24
const GENRES = ['Action', 'Adventure', 'Comedy', 'Drama', 'Fantasy', 'Horror', 'Romance', 'Sci-Fi', 'Slice of Life', 'Sports', 'Supernatural', 'Thriller']

export default function Browse() {
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('') // search text actually sent, updated on submit
  const [type, setType] = useState('')
  const [genre, setGenre] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<TitleList | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
    if (query) params.set('search', query)
    if (type) params.set('type', type)
    if (genre) params.set('genre', genre)

    let stale = false
    api<TitleList>(`/titles?${params}`)
      .then((d) => {
        if (stale) return
        setData(d)
        setError('')
      })
      .catch((e: Error) => !stale && setError(e.message))
    return () => {
      stale = true
    }
  }, [query, type, genre, page])

  const pages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1

  return (
    <div>
      <h1>Browse</h1>
      <form
        className="filters"
        onSubmit={(e) => {
          e.preventDefault()
          setPage(1)
          setQuery(search.trim())
        }}
      >
        <input placeholder="Search titles" value={search} onChange={(e) => setSearch(e.target.value)} />
        <select value={type} onChange={(e) => { setType(e.target.value); setPage(1) }}>
          <option value="">All types</option>
          <option value="Anime">Anime</option>
          <option value="Manga">Manga</option>
        </select>
        <select value={genre} onChange={(e) => { setGenre(e.target.value); setPage(1) }}>
          <option value="">All genres</option>
          {GENRES.map((g) => <option key={g}>{g}</option>)}
        </select>
        <button type="submit">Search</button>
      </form>

      {error && <p className="error">{error}</p>}
      {!data && !error && <p className="muted">Loading...</p>}
      {data && data.items.length === 0 && <p className="muted">No titles found.</p>}
      {data && (
        <>
          <div className="grid">
            {data.items.map((t) => <TitleCard key={t.id} title={t} />)}
          </div>
          <div className="pager">
            <button disabled={page <= 1} onClick={() => setPage(page - 1)}>Prev</button>
            <span className="muted">Page {page} of {pages} ({data.total} titles)</span>
            <button disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
          </div>
        </>
      )}
    </div>
  )
}
