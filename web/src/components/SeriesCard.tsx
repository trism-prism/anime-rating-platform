import { Link } from 'react-router-dom'
import type { TitleType } from '../types'

type Props = { id: number; name: string; coverUrl: string | null; type: TitleType; average: number | null; ratingCount?: number }

export default function SeriesCard({ id, name, coverUrl, type, average, ratingCount }: Props) {
  return (
    <Link to={`/series/${id}`} className="card">
      {average != null && <span className="score-chip" aria-hidden="true">{average}</span>}
      {coverUrl ? <img src={coverUrl} alt="" loading="lazy" /> : <div className="noimg" />}
      <div className="card-body">
        <div className="card-name" title={name}>{name}</div>
        <div className="muted">
          {type} · <span className="num">{average ?? '-'}</span> / 10
        </div>
        {ratingCount !== undefined && (
          <div className="muted"><span className="num">{ratingCount}</span> {ratingCount === 1 ? 'rating' : 'ratings'}</div>
        )}
      </div>
    </Link>
  )
}
