# Netcode testing

`Assets/Scripts/NetDebugTools.cs` runs in the Editor and in Development builds only. It creates itself
when the game starts, so no scene setup is needed.

## Setup
1. **Window > Multiplayer > Multiplayer Play Mode**: enable 1–3 virtual players. Each virtual player is
   a separate process, so each one can simulate its own network conditions.
2. Press Play, host a session in the main Editor and join from the virtual players.
3. In the window you want to test, press **F2** to cycle network conditions. Test a client at
   **Bad (+100ms)**: that player is the one who will feel unfair deaths.

The simulator works through Relay. It changes only the traffic of the instance where you pressed F2.

## Hotkeys
| Key | What it does |
|---|---|
| F1 | Overlay: RTT, simulated network, how far behind the host sees your ship, fair/unfair death count |
| F2 | Cycle network conditions for this instance: Off, Good, Typical, Bad, Awful |
| F3 | Host ghosts. Cyan ring = where the host thinks *you* are (magenta line = the gap); white = other ships |
| F4 | Bomb hitboxes. Yellow = not armed yet, red = lethal |

## Fair / unfair verdict
Every bomb death is judged on the **victim's** machine:
- **FAIR**: the victim's ship touched that armed bomb on their own screen before the death arrived.
- **UNFAIR**: it didn't. The verdict says by how much the bomb missed, or whether it never appeared.

The host appends every verdict to `netdebug_deaths_v2.csv` in `Application.persistentDataPath`. The full
path is printed in the console and shown in the host overlay. Columns: victim, dropper, bomb id,
verdict, the closest gap on the victim's screen, time from touch to death, the gap on the host,
bomb age, victim RTT, victim network preset and kill source (`victim_report` or `host_fallback`).

Use the unfair rate at **Bad** latency as the fairness metric. Every fix in Phase 0 should bring it down.
