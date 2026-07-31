# FlowCast Share Readiness Design

## Scope

Implement four improvements without changing the verified Voicemeeter routing path:

1. Require a detected audio signal before sharing can start.
2. Show the actual B1 switch state and the current Input signal level.
3. Add 5, 15, 30, and 60 minute timer presets.
4. Let users mark programs as favorites and keep them at the top of the list.

Global shortcuts, automatic Windows startup, and audio fade effects are out of scope.

## Share Readiness

`Voicemeeter Input` is sampled while FlowCast is idle. A selected program can only start sharing after the Input level has remained above a small non-silent threshold for consecutive samples. This avoids enabling B1 when the selected program is open but silent.

The check is only a start precondition. Once sharing has begun, temporary quiet passages do not stop sharing. Existing process-close safety reset remains responsible for stopping a closed source.

## B1 Status

FlowCast reads the actual `Strip[3].B1` and `Strip[4].B1` switches through the Voicemeeter remote API. The main card reports one of: local-only, ready to share, sharing, or routing attention required. It also displays the current Input signal level so the user can see why sharing is unavailable.

## Favorites And Timer

Favorite executable names are stored under FlowCast local application data. Favorite rows render first, then remaining programs retain a stable display-name order. Timer presets only fill the existing minute field; the user still explicitly starts the timer.

## Verification

- Unit tests cover silence detection, signal detection, and favorite ordering.
- Manual runtime verification confirms that silent Input disables sharing, active Input enables sharing, and B1 remains off until start is confirmed.
- Existing full test suite must remain green.
