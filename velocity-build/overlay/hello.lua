velocity.log("hello.lua loaded")
local elapsed = 0
function on_load() velocity.log("on_load") end
function on_frame(dt)
  elapsed = elapsed + dt
  if elapsed >= 10 then
    elapsed = 0
    velocity.log("alive " .. tostring(velocity.now_ms()))
  end
end
function on_unload() velocity.log("on_unload") end
