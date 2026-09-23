\ test-mission.fs -- a long headless mission: 3000 robot instructions with
\ the stack depth checked throughout.  A shim word with the wrong stack
\ effect leaks a few cells per use; the IBOL loop multiplies that until the
\ stack underflows into the heap and pforth segfaults (the XYPIVOT arity
\ bug did exactly that, one PICKUP at a time).
include loader.fs

decimal
back.buffer back.len@ erase
anim.buffer anim.len@ erase
ttl.buffer ttl.len@ erase
ibol.buffer@ ibol.len erase
source.buffer source.len erase

variable drift
+gfx init.chipwits start.game -gfx
: mission ( -- )
   3000 0 do
     +gfx execute.robot.instruction -gfx
     depth if depth drift !  leave then
     prog.status @ game.on@ = not if leave then
   loop ;
mission
cr drift @ if ." FAIL: stack depth " drift @ . ." after an instruction"
else ." PASS: mission ran, cycles left " cycle.ct @ . ." score " points @ . then cr
bye
