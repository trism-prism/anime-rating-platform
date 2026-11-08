import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { score } from '../format'
import type { ProfileOverview as Overview } from '../types'
import Cover from './Cover'

// top rated titles, top genres by average score and the rating count, of the ratings the viewer may see
export default function ProfileOverview({ username }: { username: string }) {
  const [o, setO] = useState<Overview | null>(null)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0)

  useEffect(() => {
    api<Overview>(`/users/${encodeURIComponent(username)}/overview`).then((r) => { setO(r); setError('') }).catch((e: Error) => setError(e.message))
  }, [username, tick])

  if (error) return <p className="error" role="alert">{error} <button onClick={() => setTick((t) => t + 1)}>Retry</button></p>
  if (!o) return null
  if (o.ratingCount === 0) return <p className="muted">No ratings yet.</p>
  return (
    <section className="overview" aria-label="Overview">
      <p className="muted">{o.ratingCount} {o.ratingCount === 1 ? 'rating' : 'ratings'}</p>
      {o.topTitles.length > 0 && (
        <>
          <h2>Top rated</h2>
          <ol className="overview-titles">
            {o.topTitles.map((t) => (
              <li key={t.ratingId}>
                <Cover src={t.coverUrl} name={t.titleName} />
                <div className="overview-main">
                  <Link to={`/series/${t.seriesId}`} className="link">{t.titleName}</Link>
                  {t.hasReview && (
                    <p className="overview-review muted">
                      {t.snippet ?? 'Spoiler review'} <Link to={`/reviews/${t.ratingId}`} className="link">Read review</Link>
                    </p>
                  )}
                </div>
                <span className="num">{score(t.overall)}</span>
              </li>
            ))}
          </ol>
        </>
      )}
      {o.topGenres.length > 0 && (
        <>
          <h2>Top genres</h2>
          <ul className="overview-genres">
            {o.topGenres.map((g) => (
              <li key={g.name}><span>{g.name}</span> <span className="num muted">{score(g.average)} avg</span></li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}
