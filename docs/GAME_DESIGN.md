# BombBattle — Game Design Plan

## Vision (decided)
**A party chaos game** for friends (Discord / couch), 4–8 players, ~3-minute matches.
Pirate ships always sail forward and leave a trail of bombs behind them; touching any bomb sinks you.
Last ship floating wins the round; first to 5 points wins the match.
Reference games: Stick Fight, Pummel Party, Fall Guys, Curve Fever.

**Network model (decided): Host + clients over Unity Relay** (Netcode for GameObjects, Client-Server topology,
sessions via Multiplayer Widgets). No dedicated server. Do not propose switching to dedicated servers or
Distributed Authority unless asked.

**Beta distribution (decided): WebGL build on itch.io.** Relay always uses secure WebSockets (WSS) on every
platform, so browser, Editor and desktop players can all play together and any of them can host (a browser can
host through Relay). This is forced in the embedded Multiplayer Widgets package
(`Packages/com.unity.multiplayer.widgets`, `SessionManager.EnterSession`) and by `UseWebSockets` on the
`UnityTransport` in `GameScene`. A browser host must keep its tab visible: a hidden tab freezes the match for
everyone, so `WebHostTabWarning` warns the host when hosting starts and again after it detects a freeze.

## Design pillars — every feature is judged against these
1. **Readable, fair deaths** — a player always understands what killed them.
2. **Big plays, small inputs** — near-misses, traps and cut-offs must feel great; controls stay simple.
3. **Laughable chaos** — fun to watch and stream; surprise over precision.
4. **The sea is the theme** — waves, wind, cannons affect gameplay, not just visuals.

## Known problems to fix
- Client-authoritative movement & bomb spawn positions → unfair deaths under latency, host advantage, cheating.
- Deaths hard to read with 8 players and hundreds of bombs.
- Low skill expression (steer + 2 abilities on long 15s/30s cooldowns).
- Dead players spectate with nothing to do.
- No retention loop.
- ✅ ~~`DefaultWidgetConfiguration.asset` MaxPlayers = 4 while `GameManager` MaxPlayers = 8~~ (both are 8 now).

## Roadmap

### Phase 0 — Trustworthy core (top priority)
- ✅ Netcode test tools (`NetDebugTools`, see `docs/NETCODE_TESTING.md`): latency simulation, host ghosts,
  hitboxes, fair/unfair death verdicts logged to CSV. Success metric: unfair death rate at "Bad" latency.
- ✅ Synchronized bomb arming: every bomb (trail + Galleon shot) is harmless for `armDelay` (0.4 s) after spawn,
  timed on the shared network clock so it turns lethal at the same moment everywhere. While arming it is small,
  pale and blinking faster (lit fuse), then pops to full size in the owner's color. Rule: `armDelay` must stay
  above worst-case one-way latency. Gameplay effect: you can't be killed by a bomb that just appeared under you.
- ✅ Bigger, forgiving bombs: visual 1.6× larger (≈1.9 m across), kill radius = 78% of the visual radius
  (≈0.75 m, was 0.6 m). Bombs look and are harder to slip between, but a visual graze is not a death.
- ✅ Spawn protection: 1.5 s at every round start on the shared network clock; protected ships pass through
  bombs (bombs stay). Ships still inside a bomb when it ends are sunk. No visual indicator (a blink was tried and
  rejected as hard on the eyes).
- ✅ Exploit/bug pass: only the host can sink a ship or remove a bomb (a client may only report its *own* wall
  hit); a ship can only die once; if the last ships sink in the same physics step the round is a **draw**
  (no point) instead of the first death handing the win to another sinking ship.
  Outside a live round (lobby, countdown, after the round is decided) and for a sunk ship, the host refuses kills,
  trail bombs and abilities; everything a round leaves on the sea (bombs, auras, waves) is cleared before the next.
- ✅ Victim-side bomb hits: **you sink only if your ship touched an armed bomb on your own screen.** The owner
  checks its hull every physics step and reports the touch; the host sanity-checks it (bomb exists, armed,
  no protection/invincibility, plausible position) and sinks the ship. A report can only sink the reporter.
  Host fallback against clients that never report: the host sinks a ship it sees ≥0.35 m deep in a bomb that was
  armed for longer than the victim's RTT + 0.2 s, if no report arrives within RTT + 0.25 s.
  Deaths within 0.15 s of each other count as simultaneous; if they sink the last ships, the round is a draw.
- ✅ Host checks movement (loose, party game): every 0.25 s, distance moved and yaw turned over the last 0.5 s must
  fit the ship's allowances (base speed, host-approved sprint, Sloop aura, Drakkar knockback, Caravel slide) +30%
  and +3 m / +25°. Two failures in a row snap the ship back to its last valid position. Sprint is applied by the
  owner instantly and reported; the host checks its cooldown. Approved teleports (round reset, slide snap) get a
  grace period. The host's own ship isn't checked.
- Host spawns trail bombs from its own view of the ship instead of client-sent positions.
- ✅ Per-ship tuning in `ShipConfig` ScriptableObjects (`Assets/ShipConfigs/<Ship>.asset`): speed, turn rate, bomb
  interval, sprint, ability cooldown and ability values. Single source of truth: the owner moves with them and the
  host checks movement/cooldowns against them. All four ships still share today's values; differentiating them
  is Phase 2.
- Unify input on the Input System (remove legacy `Input.GetKeyDown`), add gamepad support.

### Phase 1 — Feel & readability
- Death: freeze-frame, screen shake, kill feed with killer color + ship.
- Near-miss feedback (whoosh, sparkle, style point).
- ~~Fresh bombs pulse in owner color~~ (done via arming visual); nearby bombs outlined.
- Telegraph abilities (Galleon aim line, Drakkar wave preview, Caravel anchor tether).
- Dynamic music intensity by alive count; slow-mo on the final kill; camera zoom for the last duel.

### Phase 2 — Depth
- Each ship: 1 passive + 1 active, shorter cooldowns (8–15 s).
  - Galleon (heavy): bigger bombs, slow turn; active broadside.
  - Sloop (agile): sharp turns, sparser bombs; active barrel-roll through bombs.
  - Drakkar (brawler): ramming knockback; active wave (existing).
  - Caravel (trickster): anchor + slide (existing); passive semi-transparent bombs to enemies.
- New verb: brake/drift (tighter turn, but drops bombs faster).
- Kill credit to bomb owner + placement points.
- Sea pickups: powder barrel, shield, bomb-clear ring, speed sail.

### Phase 3 — Content & modes
- Arenas with one twist each: archipelago (rocks), storm (wind drift), whirlpool, canyon.
- Modes: Last Ship Floating, Treasure Hunt, Teams 2v2/4v4, Kraken (1 vs all).
- **Ghost mode**: dead players become seagull spirits that drop a small bomb / wind gust every few seconds.
- Optional "Upgrade Draft" mode: survivors pick 1 of 3 upgrades between rounds.

### Phase 4 — Retention (only once the game is fun)
- Cosmetics (sails, flags, bomb skins, trails), profile stats, light challenges.
- Quick play + private lobbies with share code.
- Bots to fill lobbies and for a solo tutorial.

### Phase 5 — Playtest & ship
- Weekly playtests with a short survey ("did you understand why you died?" 1–5).
- Analytics: pick/win rate per ship, round length, death causes.
- itch.io demo → Steam Next Fest demo.

## Immediate next steps
1. Fairness pass (host-validated movement/bombs, smaller bomb hitbox).
2. `ShipConfig` ScriptableObject refactor.
3. Kill feed + death freeze-frame + near-miss feedback.
4. Ghost mode prototype.
5. Playtest with 4+ players.
