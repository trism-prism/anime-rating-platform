import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'
import { useTitle } from '../useTitle'
import type { ListPageData } from '../types'

// keyed so another list or another account starts from the loading state
export default function ListPage() {
  const { id = '' } = useParams()
  const { user } = useAuth()
  return <List key={id + (user?.userId ?? '')} id={id} />
}

function List({ id }: { id: string }) {
  const [data, setData] = useState<ListPageData | null>(null)
  const [missing, setMissing] = useState(false)
  const [error, setError] = useState('')
  const [name, setName] = useState('')
  useTitle(data?.name)

  useEffect(() => {
    api<ListPageData>(`/lists/${id}`)
      .then((d) => { setData(d); setName(d.name) })
      .catch((e: Error) => (e instanceof ApiError && e.status === 404 ? setMissing(true) : setError(e.message)))
  }, [id])

  if (missing) return <div><h1>List not found</h1><p className="muted">It does not exist, or you can not see it.</p></div>
  if (!data) return error ? <p className="error">{error}</p> : <p className="muted">Loading</p>

  async function rename() {
    setError('')
    try {
      await api(`/me/lists/${id}`, { method: 'PUT', body: JSON.stringify({ name }) })
      setData((d) => d && { ...d, name: name.trim() })
    } catch (err) {
      setError((err as Error).message)
    }
  }

  async function remove(itemId: number) {
    setError('')
    try {
      await api(`/me/lists/${id}/items/${itemId}`, { method: 'DELETE' })
      setData((d) => d && { ...d, items: d.items.filter((i) => i.itemId !== itemId) })
    } catch (err) {
      setError((err as Error).message)
    }
  }

  return (
    <div>
      <h1>{data.name}</h1>
      <p className="muted">By <Link to={`/u/${encodeURIComponent(data.userName)}`} className="link">{data.userName}</Link> · {data.items.length} {data.items.length === 1 ? 'item' : 'items'}</p>
      {data.mine && (
        <div className="row">
          <input value={name} maxLength={60} aria-label="List name" onChange={(e) => setName(e.target.value)} />
          <button disabled={!name.trim() || name.trim() === data.name} onClick={rename}>Rename</button>
        </div>
      )}
      {error && <p className="error">{error}</p>}
      {data.items.length === 0 && <p className="muted">Nothing on this list yet. Add titles and series from their pages.</p>}
      {data.items.map((i) => (
        <div key={i.itemId} className="list-row">
          <Link to={i.titleId != null ? `/titles/${i.titleId}` : `/series/${i.seriesId}`} className="list-name">{i.name}</Link>
          <span className="muted">{i.titleId != null ? 'Title' : 'Series'}</span>
          {data.mine && <button onClick={() => remove(i.itemId)}>Remove</button>}
        </div>
      ))}
    </div>
  )
}
