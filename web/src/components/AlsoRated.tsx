import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { score } from '../format'
import { useScope, withScope } from '../scope'
import type { AlsoRatedItem } from '../types'

// "people who rated this also rated": counts only, never who. `path` is /titles/{id} or /series/{id}.
export default function AlsoRated({ path }: { path: string }) {
  const [items, setItems] = useState<AlsoRatedItem[] | null>(null)
  const [scope] = useScope()

  useEffect(() => {
    let stale = false
    api<AlsoRatedItem[]>(withScope(`${path}/also-rated`, scope)).then((r) => !stale && setItems(r)).catch(() => !stale && setItems(null))
    return () => { stale = true }
  }, [path, scope])

  if (!items || items.length === 0) return null
  return (
    <>
      <h2>People who liked this also rated</h2>
      {items.map((i) => (
        <div key={i.titleId ?? i.seriesId} className="list-row">
          <Link to={i.titleId != null ? `/titles/${i.titleId}` : `/series/${i.seriesId}`} className="list-name">{i.name}</Link>
          <span className="muted">{i.coRaters} of them rated it · <span className="nowrap">{score(i.average)} / 10</span></span>
        </div>
      ))}
    </>
  )
}
