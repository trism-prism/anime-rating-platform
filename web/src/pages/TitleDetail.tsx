import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import ScoreBar from '../components/ScoreBar'
import RateForm from '../components/RateForm'
import ListForm from '../components/ListForm'
import AddToList from '../components/AddToList'
import AlsoRated from '../components/AlsoRated'
import Cover from '../components/Cover'
import Reviews from '../components/Reviews'
import Select from '../components/Select'
import { cleanSynopsis, formatLabel, score, sentence, sizeLabel } from '../format'
import { useTitle } from '../useTitle'
import type { TitleDetail as Detail } from '../types'

export default function TitleDetail() {
  const { id } = useParams()
  const { user } = useAuth()
  const [title, setTitle] = useState<Detail | null>(null)
  const [error, setError] = useState<{ id: string; message: string } | null>(null)
  const [tick, setTick] = useState(0) // bump to refetch after rating
  const [picked, setPicked] = useState({ titleId: 0, volume: 0 }) // volume 0 = none picked
  useTitle(title && title.id === Number(id) ? title.name : null)

  useEffect(() => {
    let stale = false
    api<Detail>(`/titles/${id}`)
      .then((d) => { if (!stale) { setTitle(d); setError(null) } })
      .catch((e: Error) => !stale && id && setError({ id, message: e.message }))
    return () => {
      stale = true
    }
  }, [id, tick])
  const load = () => setTick((t) => t + 1)

  if (error && error.id === id) {
    return (
      <p className="error">
        {error.message} <button onClick={() => { setError(null); load() }}>Retry</button>
      </p>
    )
  }
  if (!title || title.id !== Number(id)) return <p className="muted">Loading</p>

  const a = title.averages
  const volume = picked.titleId === title.id ? picked.volume : 0
  const format = title.format && formatLabel(title.format).toLowerCase() !== title.type.toLowerCase() ? formatLabel(title.format) : '' // "Manga · Manga" says nothing
  const synopsis = cleanSynopsis(title.synopsis)

  // On a phone the page is one column in this order (facts, synopsis, rating, list, characters, reviews): the two
  // wrappers dissolve and `order` sorts their children. From 900px the main column is on the left, the side column
  // (rating and list, sticky) on the right. See .title-layout in index.css.
  return (
    <div>
      <div className="title-head">
        <Cover src={title.coverUrl} name={title.name} alt={title.name} eager className="title-cover" />
        <div className="title-facts">
          <p className="muted crumb">
            <Link to={`/series/${title.seriesId}`} className="link">{title.seriesName}</Link> · #{title.position}
          </p>
          <h1>{title.name}</h1>
          <div className="badges">
            <span className="badge-kind">{title.type}</span>
            {format && <span className="badge-kind">{format}</span>}
            <span className="muted">{[title.year, sizeLabel(title)].filter(Boolean).join(' · ')}</span>
          </div>
          {title.genres.length > 0 && (
            <div className="tags">
              {title.genres.map((g) => <span key={g} className="tag">{g}</span>)}
            </div>
          )}
          {a ? (
            <div className="title-score">
              <span className="num series-score-num">{score(a.overall)}</span>
              <span className="series-score-of">/ 10</span>
              <span className="muted"> from <span className="num">{title.ratingCount}</span> {title.ratingCount === 1 ? 'rating' : 'ratings'}</span>
            </div>
          ) : (
            <p className="muted">Not rated yet</p>
          )}
        </div>
      </div>

      <div className="title-layout">
        <div className="title-main">
          <div style={{ order: 10 }}>
            <p className="synopsis">{synopsis.text || 'No synopsis.'}</p>
            {synopsis.source && <p className="muted credit">Source: {synopsis.source}</p>}
          </div>

          {a && (
            <section style={{ order: 20 }} aria-labelledby="community-title">
              <h2 id="community-title">Community scores</h2>
              <div className="scores">
                <ScoreBar label="Story" value={a.story} />
                <ScoreBar label="Art" value={a.art} />
                <ScoreBar label="Characters" value={a.characters} />
                <ScoreBar label="Enjoyment" value={a.enjoyment} />
                <ScoreBar label="Overall" value={a.overall} />
              </div>
            </section>
          )}

          {title.type === 'Manga' && title.volumes != null && (
            <section style={{ order: 35 }} aria-labelledby="volume-title">
              <h2 id="volume-title">Rate a volume</h2>
              <p className="muted">Volume ratings are kept separate and do not change the title average above.</p>
              <label className="row">
                <span>Volume</span>
                <Select
                  label="Volume"
                  value={String(volume)}
                  options={[{ value: '0', label: 'Pick a volume' }, ...Array.from({ length: title.volumes }, (_, i) => ({ value: String(i + 1), label: `Volume ${i + 1}` }))]}
                  onChange={(v) => setPicked({ titleId: title.id, volume: Number(v) })}
                />
              </label>
              {volume > 0 && <RateForm key={`${title.id}-${volume}`} titleId={title.id} volume={volume} onChanged={load} />}
            </section>
          )}

          {title.volumeAverages && (
            <section style={{ order: 50 }} aria-labelledby="volavg-title">
              <h2 id="volavg-title">Volume averages</h2>
              {title.volumeAverages.length === 0 ? (
                <p className="muted">No volume ratings yet.</p>
              ) : (
                <table className="table volumes">
                  <thead><tr><th>Volume</th><th>Average</th><th>Ratings</th></tr></thead>
                  <tbody>
                    {title.volumeAverages.map((v) => (
                      <tr key={v.volume}><td>Volume {v.volume}</td><td className="num"><strong>{score(v.average)}</strong> / 10</td><td className="num">{v.count}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>
          )}

          <section style={{ order: 60 }} aria-labelledby="characters-title">
            <h2 id="characters-title">Characters</h2>
            {title.characters.length === 0 && <p className="muted">No characters listed.</p>}
            <ul className="characters">
              {title.characters.map((c) => (
                <li key={c.characterId}>
                  <Cover src={c.imageUrl} name={c.name} />
                  <div>
                    <div>{c.name}</div>
                    <div className="muted">{[c.role && sentence(c.role), c.voiceActor && `Voice: ${c.voiceActor.name}`].filter(Boolean).join(' · ')}</div>
                  </div>
                </li>
              ))}
            </ul>
          </section>

          <div style={{ order: 70 }}><AlsoRated key={`also-${title.id}`} path={`/titles/${title.id}`} /></div>

          <div style={{ order: 80 }}><Reviews key={`reviews-${title.id}`} path={`/titles/${title.id}`} showTitle={false} refresh={tick} /></div>
        </div>

        <div className="title-side">
          <section className="panel" style={{ order: 30 }} aria-labelledby="your-rating-title">
            <h2 id="your-rating-title">Your rating</h2>
            <RateForm key={`rate-${title.id}`} titleId={title.id} onChanged={load} />
          </section>

          {user && (
            <section className="panel" style={{ order: 40 }} aria-labelledby="your-list-title">
              <h2 id="your-list-title">Your list</h2>
              <ListForm key={`list-${title.id}`} titleId={title.id} />
              <AddToList key={`add-${title.id}`} titleId={title.id} bare />
            </section>
          )}
        </div>
      </div>
    </div>
  )
}
