\ test-render.fs -- render the real game screen headlessly and save PBM shots
include loader.fs

decimal
cr ." === render test ===" cr

\ scratch bitmap buffers are dictionary memory: clear them before first use
back.buffer back.len@ erase
anim.buffer anim.len@ erase
ttl.buffer ttl.len@ erase
ibol.buffer@ ibol.len erase
source.buffer source.len erase

1 adventure !
+gfx
init.chipwits          \ original startup: files, sprite sheets, robot, screen
-gfx ." init.chipwits done, depth=" depth . cr +gfx
s" shot-init.pbm" save-pbm

start.game             \ draws the gameboard with the robot in a real room
-gfx ." start.game done, depth=" depth . cr +gfx
s" shot-board.pbm" save-pbm

\ let Doug Sharp's saved robot program run a while
30 0 do execute.robot.instruction loop
-gfx ." 30 cycles done, depth=" depth .
." sq=" robot.square @ . ." fuel=" fuel.reg @ . cr +gfx
s" shot-run.pbm" save-pbm

-gfx cr ." === render test done ===" cr
bye
