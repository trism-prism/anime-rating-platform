import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { api } from '../api'
import ScoreBar from '../components/ScoreBar'
import RateForm from '../components/RateForm'
import ListForm from '../components/ListForm'
import type { TitleDetail as Detail } from '../types'

// AniList synopses contain html (<br>, <i>); show as plain text instead of injecting html
function plainText(html: string | null) {
  return (html ?? '').replace(/<br\s*\/?>/gi, '\n').replace(/<[^>]*>/g, '')
}

export default function TitleDetail() {
  const { id } = useParams()
  const [title, setTitle] = useState<Detail | null>(null)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0) // bump to refetch after rating

  useEffect(() => {
    let stale = false
    api<Detail>(`/titles/${id}`)
      .then((d) => { if (!stale) { setTitle(d); setError('') } })
      .catch((e: Error) => !stale && setError(e.message))
    return () => {
      stale = true
    }
  }, [id, tick])
  const load = () => setTick((t) => t + 1)

  if (error && !title) return <p className="error">{error}</p>
  if (!title || title.id !== Number(id)) return <p className="muted">Loading...</p>

  const a = title.averages
  const size = title.type === 'Anime' ? (title.episodes ? `${title.episodes} episodes` : '') : (title.chapters ? `${title.chapters} chapters` : '')

  return (
    <div>
      <div className="detail">
        {title.coverUrl && <img className="cover" src={title.coverUrl} alt={title.name} />}
        <div>
          <h1>{title.name}</h1>
          <p className="muted">{[title.type, size].filter(Boolean).join(' · ')}</p>
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

      <h2>Reviews</h2>
      {title.reviews.length === 0 && <p className="muted">No reviews yet.</p>}
      {title.reviews.map((r, i) => (
        <div key={i} className="review">
          <div>
            <strong>{r.userName}</strong> <span className="muted">· {r.overall} / 10 · {new Date(r.createdAt).toLocaleDateString()}</span>
          </div>
          <p>{r.reviewText}</p>
        </div>
      ))}
    </div>
  )
}
