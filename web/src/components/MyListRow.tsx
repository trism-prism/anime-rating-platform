import { useId, useState } from 'react'
import { Link } from 'react-router-dom'
import { plural, score } from '../format'
import { STATUS_OPTIONS, type Audience, type MyRating, type WatchEntry, type WatchStatus } from '../types'
import AudienceSelect from './AudienceSelect'
import Select from './Select'
import WatchDates from './WatchDates'

// one title on My List: its list entry, its rating, and its volume ratings (manga), any of which may be missing
export interface ListRow {
  titleId: number
  seriesId: number
  name: string
  seriesName: string | null // only when it differs from the title
  coverUrl: string | null
  entry: WatchEntry | null
  rating: MyRating | null
  volumes: MyRating[]
}

const NOT_ON_LIST = [{ value: '', label: 'Not on list' }, ...STATUS_OPTIONS]
const ratingKey = (r: MyRating) => `r-${r.titleId}-${r.volume ?? 0}`

export default function MyListRow({ row, busy, error, onStatus, onEntryAudience, onRatingAudience, onDates, onDatesError, onRemove }: {
  row: ListRow
  busy: (key: string) => boolean
  error: string
  onStatus: (row: ListRow, status: WatchStatus) => void
  onEntryAudience: (e: WatchEntry, a: Audience) => void
  onRatingAudience: (r: MyRating, a: Audience) => void
  onDates: (titleId: number, startedOn: string | null, finishedOn: string | null) => void
  onDatesError: (titleId: number, message: string) => void
  onRemove: (row: ListRow) => void
}) {
  const [open, setOpen] = useState(false)
  const panel = useId()
  const { entry, rating, volumes } = row
  const progress = entry && entry.progress > 0 ? (entry.type === 'Manga' ? `${plural(entry.progress, 'chapter')} read` : `${plural(entry.progress, 'episode')} watched`) : ''
  const sub = [row.seriesName, progress, !rating && volumes.length ? `${plural(volumes.length, 'volume')} rated` : ''].filter(Boolean).join(' · ')

  return (
    <li className="mylist-row" data-row={row.titleId}>
      <div className="mylist-main">
        <div className="mylist-thumb">{row.coverUrl ? <img src={row.coverUrl} alt="" loading="lazy" /> : <div className="noimg" />}</div>
        <div className="mylist-text">
          <Link to={`/titles/${row.titleId}`} className="list-name">{row.name}</Link>
          <div className="mylist-meta">
            {rating
              ? <span className="num mylist-chip" aria-label={`Your score ${score(rating.overall)}`}>{score(rating.overall)}</span>
              : !volumes.length && <Link to={`/titles/${row.titleId}`} className="link-quiet">Rate</Link>}
            {sub && <span className="muted mylist-sub" title={sub}>{sub}</span>}
          </div>
        </div>
        <div className="mylist-status">
          <Select
            label={`Status of ${row.name}`}
            value={entry?.status ?? ''}
            options={entry ? STATUS_OPTIONS : NOT_ON_LIST}
            disabled={!entry && busy(`s-${row.titleId}`)} // a listed row stays enabled while saving, so the keyboard keeps its place
            onChange={(v) => v && onStatus(row, v as WatchStatus)}
          />
        </div>
        <button type="button" className="icon-button mylist-toggle" aria-expanded={open} aria-controls={panel}
          aria-label={`Details of ${row.name}`} onClick={() => setOpen((o) => !o)}>
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M4 6.5l4 4 4-4" />
          </svg>
        </button>
      </div>
      {error && <p className="error mylist-error" role="alert">{error}</p>}
      <div id={panel} className="mylist-detail" hidden={!open}>
        {open && (
          <>
            {entry && (
              <div className="mylist-block">
                <div className="mylist-field">
                  <span>Visibility</span>
                  <AudienceSelect label={`Visibility of ${row.name}`} value={entry.audience} disabled={busy(`w-${row.titleId}`)}
                    onChange={(a) => onEntryAudience(entry, a)} />
                </div>
                <WatchDates entry={entry} onSaved={onDates} onError={(m) => onDatesError(row.titleId, m)} />
              </div>
            )}
            {rating && (
              <div className="mylist-block">
                <div className="mylist-field">
                  <span>Your rating</span>
                  <Link to={`/titles/${row.titleId}`} className="link">Edit rating</Link>
                </div>
                <dl className="mylist-scores">
                  <div><dt>Story</dt><dd className="num">{rating.story}</dd></div>
                  <div><dt>Art</dt><dd className="num">{rating.art}</dd></div>
                  <div><dt>Characters</dt><dd className="num">{rating.characters}</dd></div>
                  <div><dt>Enjoyment</dt><dd className="num">{rating.enjoyment}</dd></div>
                </dl>
                <div className="mylist-field">
                  <span>Rating visibility</span>
                  <AudienceSelect label={`Visibility of your rating of ${row.name}`} value={rating.audience} disabled={busy(ratingKey(rating))}
                    onChange={(a) => onRatingAudience(rating, a)} />
                </div>
              </div>
            )}
            {volumes.length > 0 && (
              <div className="mylist-block">
                <div className="mylist-field">
                  <span>Volumes rated</span>
                  <Link to={`/titles/${row.titleId}`} className="link">Edit ratings</Link>
                </div>
                <ul className="mylist-volumes">
                  {volumes.map((v) => (
                    <li key={v.volume}>
                      <span>Volume <span className="num">{v.volume}</span></span>
                      <span className="num mylist-chip" aria-label={`Score ${score(v.overall)}`}>{score(v.overall)}</span>
                      <AudienceSelect label={`Visibility of your rating of ${row.name} volume ${v.volume}`} value={v.audience} disabled={busy(ratingKey(v))}
                        onChange={(a) => onRatingAudience(v, a)} />
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {entry && <div><button type="button" onClick={() => onRemove(row)}>Remove from list</button></div>}
          </>
        )}
      </div>
    </li>
  )
}
