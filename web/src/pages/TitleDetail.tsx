import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api'
import ScoreBar from '../components/ScoreBar'
import RateForm from '../components/RateForm'
import ListForm from '../components/ListForm'
import Reviews from '../components/Reviews'
import type { TitleDetail as Detail } from '../types'

// AniList synopses contain html (<br>, <i>); show as plain text instead of injecting html
function plainText(html: string | null) {
  return (html ?? '').replace(/<br\s*\/?>/gi, '\n').replace(/<[^>]*>/g, '')
}

export default function TitleDetail() {
  const { id } = useParams()
  const [title, setTitle] = useState<Detail | null>(null)
  const [error, setError] = useState<{ id: string; message: string } | null>(null)
  const [tick, setTick] = useState(0) // bump to refetch after rating
  const [picked, setPicked] = useState({ titleId: 0, volume: 0 }) // volume 0 = none picked

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
  if (!title || title.id !== Number(id)) return <p className="muted">Loading...</p>

  const a = title.averages
  const volume = picked.titleId === title.id ? picked.volume : 0
  const size = title.type === 'Anime' ? (title.episodes ? `${title.episodes} episodes` : '') : (title.chapters ? `${title.chapters} chapters` : '')

  return (
    <div>
      <div className="detail">
        {title.coverUrl && <img className="cover" src={title.coverUrl} alt={title.name} />}
        <div>
          <p className="muted crumb">
            <Link to={`/series/${title.seriesId}`} className="link">{title.seriesName}</Link> · #{title.position}
          </p>
          <h1>{title.name}</h1>
          <p className="muted">{[title.type, title.format && title.format.replace(/_/g, ' '), title.year, size].filter(Boolean).join(' · ')}</p>
          <div className="tags">
            {title.genres.map((g) => <span key={g} className="tag">{g}</span>)}
          </div>
          <p className="synopsis">{plainText(title.synopsis) || 'No synopsis.'}</p>
        </div>
      </div>

      <h2>Scores ({title.ratingCount} {title.ratingCount === 1 ? 'rating' : 'ratings'})</h2>
      {a ? (
        <div className="scores">
          <ScoreBar label="Story" value={a.story} />
          <ScoreBar label="Art" value={a.art} />
          <ScoreBar label="Characters" value={a.characters} />
          <ScoreBar label="Enjoyment" value={a.enjoyment} />
          <ScoreBar label="Overall" value={a.overall} />
        </div>
      ) : (
        <p className="muted">No ratings yet.</p>
      )}

      <h2>Your rating</h2>
      <RateForm key={title.id} titleId={title.id} onChanged={load} />

      {title.type === 'Manga' && title.volumes != null && (
        <>
          <h2>Rate a volume</h2>
          <p className="muted">Volume ratings are kept separate and do not change the title average above.</p>
          <label className="row">
            <span>Volume</span>
            <select value={volume} onChange={(e) => setPicked({ titleId: title.id, volume: Number(e.target.value) })}>
              <option value={0}>Pick a volume</option>
              {Array.from({ length: title.volumes }, (_, i) => i + 1).map((v) => <option key={v} value={v}>Volume {v}</option>)}
            </select>
          </label>
          {volume > 0 && <RateForm key={`${title.id}-${volume}`} titleId={title.id} volume={volume} onChanged={load} />}
        </>
      )}

      {title.volumeAverages && (
        <>
          <h2>Volume averages</h2>
          {title.volumeAverages.length === 0 ? (
            <p className="muted">No volume ratings yet.</p>
          ) : (
            <table className="table volumes">
              <thead><tr><th>Volume</th><th>Average</th><th>Ratings</th></tr></thead>
              <tbody>
                {title.volumeAverages.map((v) => (
                  <tr key={v.volume}><td>Volume {v.volume}</td><td><strong>{v.average}</strong> / 10</td><td>{v.count}</td></tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}

      <h2>Your list</h2>
      <ListForm key={title.id} titleId={title.id} />


      <h2>Characters</h2>
      {title.characters.length === 0 && <p className="muted">No characters listed.</p>}
      <ul className="characters">
        {title.characters.map((c) => (
          <li key={c.characterId}>
            {c.imageUrl ? <img src={c.imageUrl} alt={c.name} /> : <div className="noimg" />}
            <div>
              <div>{c.name}</div>
              <div className="muted">{[c.role, c.voiceActor && `VA: ${c.voiceActor.name}`].filter(Boolean).join(' · ')}</div>
            </div>
          </li>
        ))}
      </ul>

      <Reviews key={title.id} path={`/titles/${title.id}`} showTitle={false} refresh={tick} />
    </div>
  )
}
