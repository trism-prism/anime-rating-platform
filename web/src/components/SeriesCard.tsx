import { Link } from 'react-router-dom'
import { score } from '../format'
import Cover from './Cover'
import type { TitleType } from '../types'

type Props = { id: number; name: string; coverUrl: string | null; type: TitleType; average: number | null; ratingCount?: number; eager?: boolean }

export default function SeriesCard({ id, name, coverUrl, type, average, ratingCount, eager }: Props) {
  return (
    <Link to={`/series/${id}`} className="card">
      {average != null && <span className="score-chip" aria-hidden="true">{score(average)}</span>}
      <Cover src={coverUrl} name={name} eager={eager} className="card-cover" />
      <div className="card-body">
        <div className="card-name" title={name}>{name}</div>
        <div className="muted">{type}</div>
        <div className="muted">
          {average == null ? 'Not rated yet' : (
            <>
              <span className="nowrap"><span className="num">{score(average)}</span> / 10</span>
              {ratingCount ? <> · <span className="num">{ratingCount}</span> {ratingCount === 1 ? 'rating' : 'ratings'}</> : null}
            </>
          )}
        </div>
      </div>
    </Link>
  )
}
