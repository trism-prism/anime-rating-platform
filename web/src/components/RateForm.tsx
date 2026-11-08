import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { api } from '../api'
import { bumpBrowseVersion } from '../browseCache'
import { useAuth } from '../auth'
import { score as fmt } from '../format'
import type { Audience, MyRating } from '../types'
import AudienceSelect from './AudienceSelect'
import ConfirmDialog from './ConfirmDialog'

const FIELDS = [
  { key: 'story', label: 'Story' },
  { key: 'art', label: 'Art' },
  { key: 'characters', label: 'Characters' },
  { key: 'enjoyment', label: 'Enjoyment' },
] as const
type Field = (typeof FIELDS)[number]['key']

// 0 = not answered yet. Nothing is pre-filled, so a rating is always something the person chose.
type Scores = Record<Field, number>
const blank: Scores = { story: 0, art: 0, characters: 0, enjoyment: 0 }
const NUMBERS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]

const allSet = (s: Scores) => FIELDS.every((f) => s[f.key] > 0)
// the same rule as the server: the plain average of the four
const overallOf = (s: Scores) => (s.story + s.art + s.characters + s.enjoyment) / 4

// volume: rate one volume of a manga instead of the whole title (the API keeps the two apart)
export default function RateForm({ titleId, volume = null, onChanged }: { titleId: number; volume?: number | null; onChanged: () => void }) {
  const { user, emailVerified } = useAuth()
  const { pathname } = useLocation()
  const uid = useId()
  const [scores, setScores] = useState<Scores>(blank)
  const [detail, setDetail] = useState(false)
  const [review, setReview] = useState('')
  const [audience, setAudience] = useState<Audience>('Public') // new ratings are public
  const [spoiler, setSpoiler] = useState(false)
  const [hasRating, setHasRating] = useState(false)
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirmRemove, setConfirmRemove] = useState(false)
  // which title/volume the prefill answered or failed for; Save waits for the answer so it cannot overwrite an unseen rating
  const key = `${titleId}:${volume}`
  const [loadedKey, setLoadedKey] = useState('')
  const [errorKey, setErrorKey] = useState('')
  const [attempt, setAttempt] = useState(0)
  const loaded = loadedKey === key
  const loadError = errorKey === key
  // fields the user edited before the prefill arrived: the prefill must not overwrite those, but it always fills the others
  const edited = useRef(new Set<'scores' | 'review' | 'audience' | 'spoiler'>())

  // prefill with the user's existing rating for this title
  useEffect(() => {
    if (!user) return
    const ac = new AbortController()
    edited.current = new Set()
    api<MyRating[]>('/me/ratings')
      .then((list) => {
        if (ac.signal.aborted) return
        const mine = list.find((r) => r.titleId === titleId && (r.volume ?? null) === volume)
        if (mine) {
          const e = edited.current
          if (!e.has('scores')) {
            const s = { story: mine.story, art: mine.art, characters: mine.characters, enjoyment: mine.enjoyment }
            setScores(s)
            setDetail(!FIELDS.every((f) => s[f.key] === s.story)) // four different answers are only visible in detail
          }
          // the review, audience and spoiler flag always come back: Save sends them, and the server clears text it is not given
          if (!e.has('review')) setReview(mine.reviewText ?? '')
          if (!e.has('audience')) setAudience(mine.audience)
          if (!e.has('spoiler')) setSpoiler(mine.spoiler)
          setHasRating(true)
        }
        setLoadedKey(`${titleId}:${volume}`)
      })
      .catch(() => { if (!ac.signal.aborted) setErrorKey(`${titleId}:${volume}`) })
    return () => {
      ac.abort()
    }
  }, [titleId, volume, user, attempt])

  if (!user) {
    return <p><Link to="/login" className="link">Log in</Link> to rate this title.</p>
  }

  const ratingUrl = `/titles/${titleId}/rating${volume == null ? '' : `?volume=${volume}`}`
  const complete = allSet(scores)
  const quick = complete && FIELDS.every((f) => scores[f.key] === scores.story) ? scores.story : 0
  const answered = FIELDS.filter((f) => scores[f.key] > 0).length

  const edit = (what: 'scores' | 'review' | 'audience' | 'spoiler') => { edited.current.add(what); setMsg('') }
  function setQuick(n: number) {
    edit('scores')
    setScores({ story: n, art: n, characters: n, enjoyment: n })
  }
  function setOne(f: Field, n: number) {
    edit('scores')
    setScores((s) => ({ ...s, [f]: n }))
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    if (!complete) return
    setBusy(true)
    setMsg('')
    setError('')
    try {
      await api(ratingUrl, { method: 'PUT', body: JSON.stringify({ ...scores, reviewText: review, audience, spoiler }) })
      setHasRating(true)
      setMsg('Saved')
      bumpBrowseVersion()
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
      setDetail(false)
      setReview('')
      setSpoiler(false)
      setAudience('Public')
      setHasRating(false)
      setMsg('Removed')
      bumpBrowseVersion()
      onChanged()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
      setConfirmRemove(false)
    }
  }

  return (
    <form className="rate-form" onSubmit={save}>
      <fieldset className="quick" aria-describedby={`${uid}-quick-hint`}>
        <legend>Quick rate</legend>
        <span id={`${uid}-quick-hint`} className="sr-only">Sets all four scores</span>
        <div className="quick-scale">
          {NUMBERS.map((n) => (
            <label key={n} className="quick-opt">
              <input type="radio" name={`${uid}-quick`} value={n} checked={quick === n} onChange={() => setQuick(n)} />
              <span className="num">{n}</span>
            </label>
          ))}
        </div>
      </fieldset>

      <div className="rate-overall" role="status" aria-live="polite">
        <span className="muted">Overall</span>
        {complete
          ? <span><strong className="num rate-overall-num">{fmt(overallOf(scores))}</strong> <span className="muted">/ 10</span></span>
          : <span className="muted">{answered === 0 ? 'Not rated yet' : `${answered} of 4 answered`}</span>}
      </div>

      <div>
        <button type="button" className="link-button rate-toggle" aria-expanded={detail} aria-controls={`${uid}-detail`} onClick={() => setDetail(!detail)}>
          Rate in detail <span className="muted" aria-hidden="true">{detail ? 'Hide' : 'Show'}</span>
        </button>
        <div id={`${uid}-detail`} className="rate-detail" hidden={!detail}>
          {FIELDS.map((f) => {
            const v = scores[f.key]
            return (
              <div key={f.key} className="rate-row">
                <label htmlFor={`${uid}-${f.key}`} className="score-label">{f.label}</label>
                <span className="rate-end num" aria-hidden="true">1</span>
                <input
                  id={`${uid}-${f.key}`}
                  type="range"
                  min={1}
                  max={10}
                  className={v === 0 ? 'unset' : ''}
                  value={v || 5}
                  aria-valuetext={v === 0 ? 'Not set' : String(v)}
                  onPointerDown={() => { if (v === 0) setOne(f.key, 5) }}
                  onKeyDown={(e) => {
                    // an unset slider shows 5 but 5 cannot be reached by arrows, so the first key press sets it
                    if (v === 0 && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Enter', ' '].includes(e.key)) { e.preventDefault(); setOne(f.key, 5) }
                  }}
                  onChange={(e) => setOne(f.key, Number(e.target.value))}
                />
                <span className="rate-end num" aria-hidden="true">10</span>
                <span className="score-value">{v === 0 ? '-' : v}</span>
              </div>
            )
          })}
        </div>
      </div>

      <div className="rate-review">
        <h3>Write a review (optional)</h3>
        <textarea
          aria-label="Review"
          rows={3}
          value={review}
          disabled={!emailVerified}
          onChange={(e) => { edit('review'); setReview(e.target.value) }}
        />
        {!emailVerified && (
          <p className="muted hint"><Link to="/verify" state={{ from: pathname }} className="link">Verify your email</Link> to write reviews</p>
        )}
        <label className="check">
          <input type="checkbox" checked={spoiler} disabled={!emailVerified || !review.trim()} onChange={(e) => { edit('spoiler'); setSpoiler(e.target.checked) }} /> Contains spoilers
        </label>
        <label className="row">
          <span className="muted">Visibility</span>
          <AudienceSelect label="Visibility" value={audience} onChange={(a) => { edit('audience'); setAudience(a) }} />
        </label>
      </div>

      {loadError && (
        <p className="error" role="alert">Could not load your existing rating. <button type="button" onClick={() => { setErrorKey(''); setAttempt((n) => n + 1) }}>Retry</button></p>
      )}
      <div className="row rate-actions">
        <button type="submit" className="btn-primary" disabled={busy || !loaded || !complete}>{hasRating ? 'Update' : 'Save'}</button>
        {hasRating && <button type="button" className="btn-danger" disabled={busy} onClick={() => setConfirmRemove(true)}>Remove</button>}
        <span className="rate-status muted" role="status">{msg}</span>
        {error && <span className="error" role="alert">{error}</span>}
      </div>

      <ConfirmDialog open={confirmRemove} title="Remove your rating?" confirmLabel="Remove" danger busy={busy}
        onConfirm={remove} onCancel={() => setConfirmRemove(false)}>
        <p>Your scores and review are deleted. The average updates.</p>
      </ConfirmDialog>
    </form>
  )
}
