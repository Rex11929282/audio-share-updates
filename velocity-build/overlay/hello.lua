mcb.log("hello.lua loaded")
local elapsed = 0
function on_load() mcb.log("on_load") end
function on_frame(dt)
  elapsed = elapsed + dt
  if elapsed >= 10 then
    elapsed = 0
    mcb.log("alive " .. tostring(mcb.now_ms()))
  end
end
function on_unload() mcb.log("on_unload") end
