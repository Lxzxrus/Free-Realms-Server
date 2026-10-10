r"""Evergrove's changes to the game's UI scripts (UI\ScriptsBase.bin, one compiled Lua 5.1 chunk), as a patch.

The patch is a list of edits to the official file: byte ranges replaced and our own bytes inserted. Every byte it
carries is ours (instructions, constants, counts); none is the game's. build.py writes it as scripts.patch for the
launcher, which applies it to the player's verified file (ClientMods.ApplyScriptsPatch).

Changes:
- Housing.lua, the Decorate panel's OnFocus handler: its first instruction becomes RETURN, so the panel keeps
  keyboard focus and the search box can be typed in.
- Housing.lua, MakeHousingRadialMenu: a placed part's menu gets a fifth button, Paint (the wall paint icon).
- Housing.lua, OnRadialMenuClick: Paint calls House.PlaceFixture("-" .. currentItemGuid). House.PlaceFixture sends
  the server a placement request for that id with no checks (FreeRealms.exe 0xc0b710 -> 0xac4200); the server reads
  a negative id as "paint this part" in the colour chosen on the colour bar (HousingPalette.TryParsePaintCommand).

New code is appended to a function's code and reached by changing one jump, so no existing jump moves.
"""
import struct

LUA_HEADER_SIZE = 12

# Lua 5.1 opcodes used here.
OP_MOVE, OP_LOADK, OP_LOADBOOL, OP_GETGLOBAL, OP_GETTABLE, OP_SELF = 0, 1, 2, 5, 6, 11
OP_CONCAT, OP_JMP, OP_EQ, OP_CALL, OP_RETURN = 21, 22, 23, 28, 30

MAXARG_SBX = 131071
RK_CONSTANT = 256

# A tray click and a radial button both reach Lua as numbers; ours is clear of the game's INTERACTION_* values.
PAINT_EVENT_ID = 4242.0
# icon_hsg_cust_wall_plainpaint_01_64.dds (Standard Wall Paint), in the 64 size the menu's other icons use. The game's
# icon_palette (40653) isn't among the assets the server can stream.
PAINT_ICON_ID = 29204.0

FOCUS_SOURCE, FOCUS_LINE = "@Housing.lua", 1026
MENU_SOURCE, MENU_LINE = "@Housing.lua", 881
CLICK_SOURCE, CLICK_LINE = "@Housing.lua", 779


def abc(op, a, b, c):
    return op | a << 6 | c << 14 | b << 23


def abx(op, a, bx):
    return op | a << 6 | bx << 14


def asbx(op, a, sbx):
    return abx(op, a, sbx + MAXARG_SBX)


def jmp(from_pc, to_pc):
    return asbx(OP_JMP, 0, to_pc - (from_pc + 1))


class Function:
    pass


class Reader:
    def __init__(self, data):
        self.data = data
        self.pos = LUA_HEADER_SIZE

    def u8(self):
        value = self.data[self.pos]
        self.pos += 1
        return value

    def i32(self):
        value = struct.unpack_from("<i", self.data, self.pos)[0]
        self.pos += 4
        return value

    def string(self):
        size = struct.unpack_from("<I", self.data, self.pos)[0]
        self.pos += 4
        value = self.data[self.pos:self.pos + size - 1].decode("latin1") if size else None
        self.pos += size
        return value

    def function(self, parent_source=""):
        f = Function()
        f.source = self.string() or parent_source
        f.line = self.i32()
        self.i32()
        f.nups, f.nparams, f.vararg = self.u8(), self.u8(), self.u8()
        f.maxstack_at = self.pos
        f.maxstack = self.u8()

        f.code_count_at = self.pos
        count = self.i32()
        f.code_at = self.pos
        f.code = list(struct.unpack_from(f"<{count}I", self.data, self.pos))
        self.pos += 4 * count
        f.code_end = self.pos

        f.const_count_at = self.pos
        f.consts = []
        for _ in range(self.i32()):
            kind = self.u8()
            if kind == 0:
                f.consts.append(None)
            elif kind == 1:
                f.consts.append(bool(self.u8()))
            elif kind == 3:
                f.consts.append(struct.unpack_from("<d", self.data, self.pos)[0])
                self.pos += 8
            elif kind == 4:
                f.consts.append(self.string())
            else:
                raise ValueError(f"constant type {kind}")
        f.const_end = self.pos

        f.protos = [self.function(f.source) for _ in range(self.i32())]

        f.line_count_at = self.pos
        count = self.i32()
        f.lines = list(struct.unpack_from(f"<{count}i", self.data, self.pos))
        self.pos += 4 * count
        f.line_end = self.pos

        for _ in range(self.i32()):
            self.string()
            self.i32()
            self.i32()
        for _ in range(self.i32()):
            self.string()
        return f


def walk(f):
    yield f
    for proto in f.protos:
        yield from walk(proto)


def find(root, source, line):
    found = [f for f in walk(root) if f.source == source and f.line == line]
    if len(found) != 1:
        raise ValueError(f"expected one function at {source}:{line}, found {len(found)}")
    return found[0]


def const_bytes(value):
    if isinstance(value, float):
        return b"\x03" + struct.pack("<d", value)
    encoded = value.encode("latin1") + b"\x00"
    return b"\x04" + struct.pack("<I", len(encoded)) + encoded


class FunctionEdit:
    """Our additions to one function: replaced instructions, appended code, constants and line info."""

    def __init__(self, f):
        self.f = f
        self.replaced = {}
        self.appended = []
        self.new_consts = []
        self.maxstack = f.maxstack

    def k(self, value):
        """The index of a constant, ours appended if the function hasn't got it."""
        for index, existing in enumerate(self.f.consts + self.new_consts):
            if type(existing) is type(value) and existing == value:
                return index
        self.new_consts.append(value)
        return len(self.f.consts) + len(self.new_consts) - 1

    def rk(self, value):
        index = self.k(value)
        if index >= RK_CONSTANT:
            raise ValueError("constant index too large for an RK operand")
        return RK_CONSTANT + index

    def pc(self):
        return len(self.f.code) + len(self.appended)

    def emit(self, instruction):
        self.appended.append(instruction)

    def expect(self, pc, instruction, what):
        if self.f.code[pc] != instruction:
            raise ValueError(f"{self.f.source}:{self.f.line} pc {pc} isn't {what} ({self.f.code[pc]:#010x})")

    def edits(self):
        f = self.f
        line = f.lines[-1] if f.lines else 0
        out = []
        if self.maxstack != f.maxstack:
            out.append((f.maxstack_at, 1, bytes([self.maxstack])))
        for pc, instruction in self.replaced.items():
            out.append((f.code_at + 4 * pc, 4, struct.pack("<I", instruction)))
        if self.appended:
            out.append((f.code_count_at, 4, struct.pack("<i", len(f.code) + len(self.appended))))
            out.append((f.code_end, 0, b"".join(struct.pack("<I", i) for i in self.appended)))
            # Lua checks a function's line info is as long as its code.
            if f.lines:
                out.append((f.line_count_at, 4, struct.pack("<i", len(f.lines) + len(self.appended))))
                out.append((f.line_end, 0, struct.pack("<i", line) * len(self.appended)))
        if self.new_consts:
            out.append((f.const_count_at, 4, struct.pack("<i", len(f.consts) + len(self.new_consts))))
            out.append((f.const_end, 0, b"".join(const_bytes(value) for value in self.new_consts)))
        return out


def focus_edit(root):
    f = find(root, FOCUS_SOURCE, FOCUS_LINE)
    edit = FunctionEdit(f)
    return_instruction = f.code[-1]
    if return_instruction != abc(OP_RETURN, 0, 1, 0):
        raise ValueError("the panel's OnFocus doesn't end in RETURN")
    edit.replaced[0] = return_instruction
    return edit


def menu_edit(root):
    """MakeHousingRadialMenu: four buttons become five; the fifth, Paint, is set after the fourth."""
    f = find(root, MENU_SOURCE, MENU_LINE)
    edit = FunctionEdit(f)

    # pc 56: LOADK r6, 4 (setButtonCount(4)); pc 101: the fourth setButton's CALL r4, 6 args; pc 102 onPopup.
    edit.expect(56, abx(OP_LOADK, 6, f.consts.index(4.0)), "LOADK r6, 4")
    edit.expect(101, abc(OP_CALL, 4, 7, 1), "the fourth setButton call")
    edit.replaced[56] = abx(OP_LOADK, 6, edit.k(5.0))

    start = edit.pc()
    edit.replaced[101] = jmp(101, start)
    edit.emit(abc(OP_CALL, 4, 7, 1))                                   # the displaced call
    edit.emit(abx(OP_GETGLOBAL, 4, edit.k("HousingRadialMenu")))
    edit.emit(abc(OP_SELF, 4, 4, edit.rk("setButton")))
    edit.emit(abx(OP_LOADK, 6, edit.k(4.0)))                           # button index
    edit.emit(abx(OP_LOADK, 7, edit.k(PAINT_ICON_ID)))
    edit.emit(abx(OP_LOADK, 8, edit.k("Paint")))
    edit.emit(abx(OP_LOADK, 9, edit.k(PAINT_EVENT_ID)))
    edit.emit(abc(OP_LOADBOOL, 10, 0, 0))                              # false: the menu closes after a click
    edit.emit(abc(OP_CALL, 4, 7, 1))
    edit.emit(jmp(edit.pc(), 102))
    edit.emit(abc(OP_RETURN, 0, 1, 0))                                 # never reached: a function must end in RETURN
    edit.maxstack = max(f.maxstack, 11)
    return edit


def click_edit(root):
    """OnRadialMenuClick: after the game's buttons, Paint asks the server to paint the selected part."""
    f = find(root, CLICK_SOURCE, CLICK_LINE)
    edit = FunctionEdit(f)

    # pc 34: the last button's "not this one" jump to the RETURN at pc 40.
    edit.expect(34, jmp(34, 40), "JMP to the final RETURN")
    edit.expect(40, abc(OP_RETURN, 0, 1, 0), "RETURN")

    start = edit.pc()
    edit.replaced[34] = jmp(34, start)
    edit.emit(abc(OP_EQ, 0, 1, edit.rk(PAINT_EVENT_ID)))                # r1 == Paint: skip the next jump
    edit.emit(jmp(edit.pc(), 40))
    edit.emit(abx(OP_GETGLOBAL, 2, edit.k("House")))
    edit.emit(abc(OP_GETTABLE, 2, 2, edit.rk("PlaceFixture")))
    edit.emit(abx(OP_LOADK, 3, edit.k("-")))
    edit.emit(abx(OP_GETGLOBAL, 4, edit.k("currentItemGuid")))
    edit.emit(abc(OP_CONCAT, 3, 3, 4))
    edit.emit(abc(OP_CALL, 2, 2, 1))
    edit.emit(abc(OP_RETURN, 0, 1, 0))
    edit.maxstack = max(f.maxstack, 5)
    return edit


def make_patch(original):
    """The edits, as (offset, length replaced, bytes inserted), for the official file."""
    root = Reader(original).function()
    edits = []
    for edit in (focus_edit(root), menu_edit(root), click_edit(root)):
        edits.extend(edit.edits())
    # By offset; at one offset an insertion (length 0) comes before the replacement that starts there, since the
    # code ends where the constant count begins.
    edits.sort(key=lambda e: (e[0], e[1]))
    for (offset, length, _), (next_offset, _, _) in zip(edits, edits[1:]):
        if offset + length > next_offset:
            raise ValueError("overlapping edits")
    return edits


def apply_patch(original, edits):
    # Last first, so earlier offsets still hold; at one offset the replacement goes before the insertion.
    data = bytearray(original)
    for offset, length, inserted in sorted(edits, key=lambda e: (e[0], e[1]), reverse=True):
        data[offset:offset + length] = inserted
    return bytes(data)


def serialize_patch(edits):
    """scripts.patch: "EVGP", a count, then per edit its offset, the length replaced, and the bytes inserted."""
    out = [b"EVGP", struct.pack("<I", len(edits))]
    for offset, length, inserted in edits:
        out.append(struct.pack("<III", offset, length, len(inserted)))
        out.append(inserted)
    return b"".join(out)
