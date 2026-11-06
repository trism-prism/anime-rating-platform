import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import type { ListSummary } from '../types'

// the custom lists on a profile that the viewer may see
export default function ProfileLists({ username }: { username: string }) {
  const [lists, setLists] = useState<ListSummary[] | null>(null)

  useEffect(() => {
    api<ListSummary[]>(`/users/${encodeURIComponent(username)}/lists`).then(setLists).catch(() => setLists([]))
  }, [username])

  if (!lists || lists.length === 0) return null
  return (
    <>
      <h2>Lists</h2>
      {lists.map((l) => (
        <div key={l.id} className="list-row">
          <Link to={`/lists/${l.id}`} className="list-name">{l.name}</Link>
          <span className="muted">{l.itemCount} {l.itemCount === 1 ? 'item' : 'items'}</span>
        </div>
      ))}
    </>
  )
}
