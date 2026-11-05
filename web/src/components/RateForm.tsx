import { useEffect, useState, type FormEvent } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import type { MyRating } from '../types'

const FIELDS = ['story', 'art', 'characters', 'enjoyment'] as const
type Field = (typeof FIELDS)[number]

const blank: Record<Field, number> = { story: 5, art: 5, characters: 5, enjoyment: 5 }

// volume: rate one volume of a manga instead of the whole title (the API keeps the two apart)
export default function RateForm({ titleId, volume = null, onChanged }: { titleId: number; volume?: number | null; onChanged: () => void }) {
  const { user, emailVerified } = useAuth()
  const { pathname } = useLocation()
  const [scores, setScores] = useState(blank)
  const [review, setReview] = useState('')
  const [hasRating, setHasRating] = useState(false)
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  // prefill with the user's existing rating for this title
  useEffect(() => {
    if (!user) return
    let stale = false
    api<MyRating[]>('/me/ratings')
      .then((list) => {
        const mine = list.find((r) => r.titleId === titleId && (r.volume ?? null) === volume)
        if (stale || !mine) return
        setScores({ story: mine.story, art: mine.art, characters: mine.characters, enjoyment: mine.enjoyment })
        setReview(mine.reviewText ?? '')
        setHasRating(true)
      })
      .catch(() => {})
    return () => {
      stale = true
    }
  }, [titleId, volume, user])

  if (!user) {
    return <p><Link to="/login" className="link">Log in</Link> to rate this title.</p>
  }

  const ratingUrl = `/titles/${titleId}/rating${volume == null ? '' : `?volume=${volume}`}`

  async function save(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setMsg('')
    setError('')
    try {
      await api(ratingUrl, { method: 'PUT', body: JSON.stringify({ ...scores, reviewText: review }) })
      setHasRating(true)
      setMsg('Saved.')
      onChanged()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    setBusy(true)
    setMsg('')
    setError('')
    try {
      await api(ratingUrl, { method: 'DELETE' })
      setScores(blank)
      setReview('')
      setHasRating(false)
      setMsg('Rating deleted.')
      onChanged()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="rate-form" onSubmit={save}>
      {FIELDS.map((f) => (
        <label key={f} className="rate-row">
          <span className="score-label">{f[0].toUpperCase() + f.slice(1)}</span>
          <input
            type="range"
            min={1}
            max={10}
            value={scores[f]}
            onChange={(e) => setScores({ ...scores, [f]: Number(e.target.value) })}
          />
          <span className="score-value">{scores[f]}</span>
        </label>
      ))}
      <textarea
        placeholder="Review (optional)"
        rows={4}
        value={review}
        disabled={!emailVerified}
        onChange={(e) => setReview(e.target.value)}
      />
      {!emailVerified && (
        <p className="muted hint"><Link to="/verify" state={{ from: pathname }} className="link">Verify your email</Link> to write reviews</p>
      )}
      <div className="row">
        <button type="submit" disabled={busy}>{hasRating ? 'Update rating' : 'Save rating'}</button>
        {hasRating && <button type="button" disabled={busy} onClick={remove}>Delete</button>}
        {msg && <span className="muted">{msg}</span>}
        {error && <span className="error">{error}</span>}
      </div>
    </form>
  )
}
