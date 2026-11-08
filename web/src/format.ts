// shared text formatting, so every page words counts, formats, scores and dates the same way

export const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`

// API enums arrive as TV_SHORT, SIDE_STORY and so on
export const sentence = (s: string) => s.replace(/_/g, ' ').toLowerCase().replace(/^./, (c) => c.toUpperCase())

const FORMATS: Record<string, string> = {
  TV: 'TV', TV_SHORT: 'TV short', MOVIE: 'Movie', SPECIAL: 'Special', OVA: 'OVA', ONA: 'ONA',
  MUSIC: 'Music', MANGA: 'Manga', NOVEL: 'Novel', ONE_SHOT: 'One-shot',
}
export const formatLabel = (f: string) => FORMATS[f.toUpperCase()] ?? sentence(f)

// "12 episodes", "1 volume", "3 chapters"; empty when nothing is known
export function sizeLabel(x: { episodes?: number | null; volumes?: number | null; chapters?: number | null }) {
  if (x.episodes) return plural(x.episodes, 'episode')
  if (x.volumes) return plural(x.volumes, 'volume')
  if (x.chapters) return plural(x.chapters, 'chapter')
  return ''
}

// one decimal everywhere: 6.5, 7.0
export const score = (n: number) => n.toFixed(1)

// "7 November 2026"
export const longDate = (iso: string) => new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })

// AniList synopses contain html (<br>, <i>) and often end with "(Source: X)": plain text and the credit apart
export function cleanSynopsis(html: string | null): { text: string; source: string | null } {
  // DOMParser builds an inert document (no scripts run, nothing is attached); textContent drops tags and decodes entities
  let text = new DOMParser().parseFromString((html ?? '').replace(/<br\s*\/?>/gi, '\n'), 'text/html').documentElement.textContent ?? ''
  let source: string | null = null
  text = text.replace(/\(\s*Source:\s*([^)]*?)\s*\)/gi, (_, s: string) => { source ??= s || null; return '' })
  text = text.replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim()
  return { text, source }
}
