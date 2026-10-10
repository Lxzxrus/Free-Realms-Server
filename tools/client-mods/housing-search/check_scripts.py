r"""Checks lua_patch.py's edits in a real Lua 5.1: the patched chunk must load (Lua 5.1's loader verifies bytecode, as
the game's does), and the part menu's two functions are run against stubs. Run it after changing lua_patch.py:

  python check_scripts.py <original UI\ScriptsBase.bin>

Needs lupa (pip install lupa), which bundles Lua 5.1. The game's chunk has a 4-byte size_t and this Lua an 8-byte one,
so strings are rewritten with 8-byte lengths for the check; nothing else changes.
"""
import os
import struct
import sys

import lupa.lua51 as lua51

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lua_patch  # noqa: E402


def widen(data):
    out = bytearray(data[:12])
    out[8] = 8
    pos = 12

    def u8():
        nonlocal pos
        v = data[pos]
        pos += 1
        out.append(v)
        return v

    def raw(n):
        nonlocal pos
        out.extend(data[pos:pos + n])
        pos += n

    def i32():
        nonlocal pos
        v = struct.unpack_from("<i", data, pos)[0]
        raw(4)
        return v

    def string():
        nonlocal pos
        n = struct.unpack_from("<I", data, pos)[0]
        pos += 4
        out.extend(struct.pack("<Q", n))
        raw(n)

    def function():
        string()
        i32(); i32()
        u8(); u8(); u8(); u8()
        n = i32(); raw(4 * n)
        for _ in range(i32()):
            t = u8()
            if t == 1:
                u8()
            elif t == 3:
                raw(8)
            elif t == 4:
                string()
        for _ in range(i32()):
            function()
        n = i32(); raw(4 * n)
        for _ in range(i32()):
            string(); i32(); i32()
        for _ in range(i32()):
            string()

    function()
    assert pos == len(data), (pos, len(data))
    return bytes(out)


original = open(sys.argv[1], "rb").read()
patched = lua_patch.apply_patch(original, lua_patch.make_patch(original))

for name, chunk in (("original", original), ("patched", patched)):
    lua = lua51.LuaRuntime(encoding=None)
    load = lua.eval("function(s) local f, err = loadstring(s, '=ScriptsBase') return f ~= nil, err end")
    ok, err = load(widen(chunk))
    print(name, "loads:", ok, err)

# Run the two changed functions. The main chunk defines everything at load, against a stub world.
lua = lua51.LuaRuntime(encoding=None)
harness = lua.execute(r"""
local calls = {}
local function log(...) local t = {} for i = 1, select('#', ...) do t[#t + 1] = tostring((select(i, ...))) end calls[#calls + 1] = table.concat(t, ' ') end
local stub
local stubmeta = {
  __index = function(t, k) return stub end,
  __call = function() return stub end,
  __concat = function(a, b) return tostring(a) .. tostring(b) end,
  __tostring = function() return 'stub' end,
}
stub = setmetatable({}, stubmeta)
local env = setmetatable({}, {__index = function(t, k)
  local v = _G[k]
  if v ~= nil then return v end
  return stub
end})
return {
  env = env, calls = calls, log = log, stub = stub,
}
""")
env = harness[b"env"]
log = harness[b"log"]
loader = lua.eval("function(s, env) local f, err = loadstring(s, '=ScriptsBase') if not f then error(err) end setfenv(f, env) return f end")
main = loader(widen(patched), env)
ok = lua.eval("function(f) return pcall(f) end")(main)
print("main chunk ran:", ok)

setup = lua.eval(r"""
function(env, log)
  env.HousingRadialMenu = setmetatable({}, {__index = function(t, k)
    return function(self, ...) log('HousingRadialMenu.' .. k, ...) end
  end})
  env.House = {
    IsActionPermitted = function() return 1 end,
    IsFixtureMembersOnly = function() return 0 end,
    PlaceFixture = function(...) log('House.PlaceFixture', ...) end,
    PickupFixture = function(...) log('House.PickupFixture', ...) end,
    RotateFixtureClockwise = function(...) log('House.RotateFixtureClockwise', ...) end,
  }
  env.Ui = { GetMousePos = function() return 10, 20 end, IsMember = function() return 1 end, GetString = function(k) return k end }
  env.print = function() end
  env.currentItemGuid = '1235'
  local H = env.Housing
  local results = {}
  results[1] = pcall(H.MakeHousingRadialMenu, H)
  results[2] = pcall(H.OnRadialMenuClick, H, 4242)
  results[3] = pcall(H.OnRadialMenuClick, H, H.INTERACTION_ROTATE_RIGHT)
  results[4] = pcall(H.OnRadialMenuClick, H, 999)
  return results[1], results[2], results[3], results[4]
end
""")
print("calls ok:", setup(env, log))
for line in harness[b"calls"].values():
    print("  ", line.decode() if isinstance(line, bytes) else line)
