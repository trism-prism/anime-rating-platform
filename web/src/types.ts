export type TitleType = 'Anime' | 'Manga'

export interface SeriesSummary {
  id: number
  name: string
  type: TitleType
  coverUrl: string | null
  popularity: number
  entryCount: number
  average: number | null
  ratingCount: number
  genres: string[]
}

// keyset page: pass nextCursor back as ?cursor= until it is null
export interface SeriesPage {
  items: SeriesSummary[]
  nextCursor: string | null
}

export interface Suggestion {
  id: number
  name: string
  kind: TitleType
}

export interface SeriesEntry {
  id: number
  name: string
  format: string | null
  year: number | null
  position: number
  volumes: number | null
  episodes: number | null
  chapters: number | null
  coverUrl: string | null
  average: number | null
  ratingCount: number
}

export interface SeriesDetail {
  id: number
  name: string
  type: TitleType
  coverUrl: string | null
  average: number | null
  ratingCount: number
  entries: SeriesEntry[]
  related: { type: string; items: { titleId: number; name: string; seriesId: number; seriesName: string }[] }[]
  crossLinks: { seriesId: number; name: string; type: TitleType }[]
}

export interface VoiceActor {
  id: number
  name: string
  imageUrl: string | null
}

export interface TitleCharacter {
  characterId: number
  name: string
  imageUrl: string | null
  role: string | null
  voiceActor: VoiceActor | null
}

export interface Scores {
  story: number
  art: number
  characters: number
  enjoyment: number
  overall: number
}

export interface Review {
  userName: string
  volume: number | null
  overall: number
  story: number
  art: number
  characters: number
  enjoyment: number
  reviewText: string
  createdAt: string
}

export interface VolumeAverage {
  volume: number
  average: number
  count: number
}

export interface TitleDetail {
  id: number
  aniListId: number
  type: TitleType
  name: string
  coverUrl: string | null
  synopsis: string | null
  episodes: number | null
  chapters: number | null
  genres: string[]
  popularity: number
  characters: TitleCharacter[]
  averages: Scores | null
  ratingCount: number
  reviews: Review[]
  seriesId: number
  seriesName: string
  position: number
  format: string | null
  year: number | null
  volumes: number | null
  // null when the title has no volume ratings and no known volume count
  volumeAverages: VolumeAverage[] | null
}

export interface MyRating {
  titleId: number
  volume: number | null
  seriesId: number
  titleName: string
  coverUrl: string | null
  story: number
  art: number
  characters: number
  enjoyment: number
  overall: number
  reviewText: string | null
  createdAt: string
}

export type WatchStatus = 'Planning' | 'Watching' | 'Completed' | 'Dropped'
export const STATUSES: WatchStatus[] = ['Planning', 'Watching', 'Completed', 'Dropped']

export interface WatchEntry {
  titleId: number
  seriesId: number
  seriesName: string
  name: string
  coverUrl: string | null
  type: TitleType
  status: WatchStatus
  progress: number
}

// trending and rankings are per series; id is the series id
export interface TrendingItem {
  id: number
  name: string
  coverUrl: string | null
  type: TitleType
  averageOverall: number | null
}

export interface RankingRow {
  id: number
  name: string
  coverUrl: string | null
  type: TitleType
  average: number
  ratingCount: number
}
