import { Link } from 'react-router-dom'
import type { TitleSummary } from '../types'

export default function TitleCard({ title }: { title: Pick<TitleSummary, 'id' | 'name' | 'coverUrl' | 'type' | 'averageOverall'> }) {
  return (
    <Link to={`/titles/${title.id}`} className="card">
      {title.coverUrl ? <img src={title.coverUrl} alt={title.name} loading="lazy" /> : <div className="noimg" />}
      <div className="card-body">
        <div className="card-name">{title.name}</div>
        <div className="muted">
          {title.type} · {title.averageOverall ?? '-'} / 10
        </div>
      </div>
    </Link>
  )
}
