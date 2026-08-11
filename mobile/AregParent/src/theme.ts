/**
 * One place that decides what colour anything is.
 *
 * Before this, 268 hex literals were spread across fourteen screens, which is
 * why the phone app and the parent dashboard drifted into looking like two
 * different products. The names and values below are taken from the web
 * dashboard's own CSS tokens (`wwwroot/parent.html`, `:root`), so "brand" means
 * the same colour in both places and changing it changes it once.
 *
 * The palette is Armenian manuscript: lapis and cinnabar and gold on vellum,
 * rather than the generic blue-on-white it replaces.
 *
 * Roles, not colours. Call it `theme.danger`, never `theme.red` — the point of
 * a name is that it survives a repaint.
 */
export const theme = {
  // --- ink -----------------------------------------------------------------
  /** Body text. Not black: on a warm page pure black reads as a hole. */
  ink: '#241E12',
  /** Secondary text — labels, captions, the quieter half of a row. */
  inkMuted: '#6A5B3E',
  /** Placeholder and hint text. The faintest thing still meant to be read. */
  inkHint: '#726241',
  /** Text ON a brand-coloured surface. */
  onBrand: '#FFFDF8',

  // --- structure -----------------------------------------------------------
  /** Headers, links, active states. Lapis. */
  brand: '#25417D',
  /** The stronger lapis, for filled buttons. */
  brandStrong: '#2A4A8F',
  /**
   * Brand at low strength. ONLY for a genuinely selected control — the active
   * language pill, the chosen Wi-Fi network — where it pairs with a brand
   * border and means "this one".
   *
   * Not for panels. It began life doing both jobs, which put a cool blue tile
   * in the middle of a warm page; the two were only ever the same colour by
   * accident of the old palette.
   */
  brandTint: '#E9EDF6',

  // --- surfaces ------------------------------------------------------------
  /** The page behind everything. Vellum, not white. */
  page: '#F7F1E3',
  /** Cards and inputs sitting on the page. */
  surface: '#FFFDF8',
  /** A panel that needs to sit back slightly from a card. */
  surfaceSunken: '#FBF6EC',
  /**
   * A panel that needs to stand OUT from a card — the conversations tile, a
   * highlighted row. The web dashboard's `--surface-tint`; warm, because the
   * page is vellum and a cool tile on it reads as a foreign element.
   */
  surfaceTint: '#F4EDDF',
  /** Borders. */
  line: '#E2D6BC',
  /** The barely-there divider between rows. */
  lineSoft: '#EADFC8',
  /** An input's border, which must be findable. */
  lineInput: '#D8C9A8',

  // --- states --------------------------------------------------------------
  /** Something is fine / the toy is well. Foliage. */
  ok: '#2C5233',
  okBg: '#EBF2E7',
  okLine: '#A9C4A2',
  /** Attention, not alarm — a flagged message, a warning. */
  warn: '#7A5410',
  warnBg: '#FAF0D6',
  warnLine: '#D8AE55',
  /** Destructive and error. Cinnabar. */
  danger: '#A32D22',
  /** The deeper pomegranate, for a truly irreversible action. */
  dangerDeep: '#7E2547',
  dangerBg: '#F8E3E3',
  dangerLine: '#D9A7A0',
} as const;

export type Theme = typeof theme;
