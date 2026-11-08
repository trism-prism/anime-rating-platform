// what the API sends as the name of a deleted account; it has no profile to link to
export const DELETED_USER = 'Deleted user'

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

export type Audience = 'Public' | 'Friends' | 'OnlyMe'
export type ReactionKind = 'Helpful' | 'Unhelpful' | 'Funny'
export const REACTIONS: ReactionKind[] = ['Helpful', 'Unhelpful', 'Funny']

// the part of a review a reaction changes; every reaction call answers with it
export interface ReactionState {
  myReaction: ReactionKind | null
  helpful: number
  unhelpful: number
  funny: number
}

// a rating with text, as the review lists send it (username only, no ids beyond what the page needs)
export interface ReviewItem extends ReactionState {
  id: number
  titleId: number
  titleName: string
  seriesId: number
  volume: number | null
  userName: string
  mine: boolean
  overall: number
  story: number
  art: number
  characters: number
  enjoyment: number
  reviewText: string
  spoiler: boolean // the client collapses the text; the API still sends it
  createdAt: string
  replyCount: number
}

// keyset page: pass nextCursor back as ?cursor= until it is null
export interface ReviewPage {
  items: ReviewItem[]
  nextCursor: string | null
}

export interface ReplyItem {
  id: number
  userName: string
  body: string
  spoiler: boolean
  createdAt: string
  mine: boolean
  canDelete: boolean
}

export interface ReplyPage {
  items: ReplyItem[]
  nextCursor: number | null
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
  id: number
  audience: Audience
  spoiler: boolean
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
  audience: Audience
  startedOn: string | null
  finishedOn: string | null
  rewatchCount: number
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

export type FriendState = 'None' | 'OutgoingPending' | 'IncomingPending' | 'Friends' | 'BlockedByViewer' | 'Self'

export interface Profile {
  userName: string
  joinedAt: string
  friendCount: number
  viewerState: FriendState | null // null when nobody is signed in
  requestId: number | null // set while a request between the two is pending
  bio: string | null
  hasAvatar: boolean
}

export interface ProfileOverview {
  ratingCount: number
  topTitles: { ratingId: number; titleId: number; titleName: string; seriesId: number; coverUrl: string | null; overall: number; hasReview: boolean; spoiler: boolean; snippet: string | null }[]
  topGenres: { name: string; average: number; count: number }[]
}

export interface FriendsData {
  friends: { userName: string; since: string }[]
  incoming: { requestId: number; userName: string; sentAt: string }[]
  outgoing: { requestId: number; userName: string; sentAt: string }[]
}

export interface Notification {
  id: number
  type: 'FriendRequest' | 'FriendAccepted' | 'Reply' | 'Reaction' | 'ReportOutcome'
  actor: string
  createdAt: string
  read: boolean
  actionable: boolean // a friend request that can still be answered
  requestId: number | null
  reviewId: number | null // Reply and Reaction: the review it is about
  outcome: 'Dismissed' | 'Removed' | null // ReportOutcome: what happened to the report
}

export interface NotificationPage {
  items: Notification[]
  nextCursor: number | null
}

export interface NotificationPref {
  type: string
  enabled: boolean
}

export interface FeedItem {
  ratingId: number
  userName: string
  titleId: number
  titleName: string
  volume: number | null
  overall: number
  createdAt: string
  hasReview: boolean
  spoiler: boolean
  snippet: string | null
}

export interface FeedPage {
  items: FeedItem[]
  nextCursor: number | null
}

export interface ReportItem {
  id: number
  target: 'Review' | 'Reply' | 'Profile'
  targetId: number
  reason: string
  note: string | null
  snapshot: string
  createdAt: string
  reporter: string
  targetUser: string
  bio: string | null // profile reports: what the profile shows now
  hasAvatar: boolean
}

export interface ReportPage {
  items: ReportItem[]
  nextCursor: number | null
}

export interface ListSummary {
  id: number
  name: string
  audience?: Audience
  itemCount: number
}

export interface ListItem {
  itemId: number
  titleId: number | null
  seriesId: number
  name: string
  coverUrl: string | null
}

export interface ListPageData {
  id: number
  name: string
  audience: Audience
  userName: string
  mine: boolean
  items: ListItem[]
}

export interface StatsData {
  statusCounts: Record<string, number>
  ratingCount: number
  averageScore: number | null
  distribution: number[]
  genres: { name: string; count: number }[]
  episodes: number
  chapters: number
  volumesRated: number
}

// titleId is set on a title page's list, seriesId alone on a series page's
export interface AlsoRatedItem {
  titleId?: number
  seriesId: number
  name: string
  coverUrl: string | null
  coRaters: number
  average: number
}

export interface ListImportStatus {
  running: boolean
  result: { added: number; alreadyOnList: number; unmatched: number } | null
  error: string | null
}
