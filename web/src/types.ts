export type TitleType = 'Anime' | 'Manga'

export interface TitleSummary {
  id: number
  name: string
  coverUrl: string | null
  type: TitleType
  genres: string[]
  popularity: number
  averageOverall: number | null
  ratingCount: number
}

export interface TitleList {
  total: number
  items: TitleSummary[]
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
  overall: number
  story: number
  art: number
  characters: number
  enjoyment: number
  reviewText: string
  createdAt: string
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
}

export interface MyRating {
  titleId: number
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
  name: string
  coverUrl: string | null
  type: TitleType
  status: WatchStatus
  progress: number
}

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
