-- MCB Lua Snow HUD test
-- If you can see falling snow + the status label, Lua import/on_frame/HUD drawing all work.

local flakes = {}
local count = 42
local t = 0.0

local function reset_flake(f, w, h, from_top)
    f.x = math.random() * math.max(w, 1)
    f.y = from_top and (-math.random() * 120.0) or (math.random() * math.max(h, 1))
    f.speed = 28.0 + math.random() * 72.0
    f.drift = 10.0 + math.random() * 26.0
    f.phase = math.random() * 6.28318530718
    f.size = 2.5 + math.random() * 4.0
    f.alpha = 125 + math.floor(math.random() * 110)
end

local function init_flakes()
    local w, h = velocity.screen_size()
    flakes = {}
    for i = 1, count do
        local f = {}
        reset_flake(f, w, h, false)
        flakes[i] = f
    end
end

function on_load()
    math.randomseed(velocity.now_ms() % 2147483647)
    init_flakes()
    velocity.log("雪花 HUD 已啟動")
end

function on_frame(dt)
    local w, h = velocity.screen_size()
    if w <= 0 or h <= 0 then return end

    t = t + dt

    for i = 1, #flakes do
        local f = flakes[i]
        f.y = f.y + f.speed * dt
        f.x = f.x + math.sin(t * 0.9 + f.phase) * f.drift * dt

        if f.y > h + 12 or f.x < -30 or f.x > w + 30 then
            reset_flake(f, w, h, true)
        end

        local r = f.size
        local a = f.alpha
        for arm = 0, 2 do
            local angle = arm * 1.0471975512
            local dx = math.cos(angle) * r
            local dy = math.sin(angle) * r
            velocity.hud_line(f.x - dx, f.y - dy, f.x + dx, f.y + dy, 225, 242, 255, a, 1.0)
        end
    end

    velocity.hud_text(18, 18, "Lua 雪花 HUD：運行中", 225, 242, 255, 235)
end

function on_unload()
    velocity.log("雪花 HUD 已停止")
end
