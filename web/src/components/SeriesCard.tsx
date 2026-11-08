import { Link } from 'react-router-dom'
import { score } from '../format'
import type { TitleType } from '../types'

type Props = { id: number; name: string; coverUrl: string | null; type: TitleType; average: number | null; ratingCount?: number }

export default function SeriesCard({ id, name, coverUrl, type, average, ratingCount }: Props) {
  return (
    <Link to={`/series/${id}`} className="card">
      {average != null && <span className="score-chip" aria-hidden="true">{score(average)}</span>}
      {coverUrl ? <img src={coverUrl} alt="" loading="lazy" /> : <div className="noimg" />}
      <div className="card-body">
        <h2 className="card-name" title={name}>{name}</h2>
        <div className="muted">{type}</div>
        <div className="muted">
          {average == null ? 'Not rated yet' : (
            <>
              <span className="num">{score(average)}</span> / 10
              {ratingCount ? <> · <span className="num">{ratingCount}</span> {ratingCount === 1 ? 'rating' : 'ratings'}</> : null}
            </>
          )}
        </div>
      </div>
    </Link>
  )
}
